export const meta = {
  name: 'liveolator-expert-review',
  description: 'Ten-expert read-only review of Liveolator: map, bugs, recommendations, missing features across 10 subsystems, with adversarial verification of every High/Critical bug. Optional focus hint via args.',
  phases: [
    { title: 'Review', detail: 'one expert reviewer per subsystem, read-only, structured findings' },
    { title: 'Verify', detail: 'adversarially confirm each High/Critical bug against code' },
  ],
}

// Optional focus: pass a string via Workflow `args` (e.g. "the synced decks keep drifting").
// Every reviewer prioritizes evidence relevant to it but still covers their whole lens.
const FOCUS = typeof args === 'string' && args.trim() ? args.trim()
  : (args && typeof args.focus === 'string' && args.focus.trim() ? args.focus.trim() : null)

const FINDINGS_SCHEMA = {
  type: 'object', additionalProperties: false,
  required: ['subsystem', 'persona', 'summary', 'map', 'bugs', 'recommendations', 'missingFeatures'],
  properties: {
    subsystem: { type: 'string' },
    persona: { type: 'string' },
    summary: { type: 'string', description: '3-5 sentence professional verdict on the state of this subsystem' },
    map: {
      type: 'array', description: 'What actually exists in code for this subsystem',
      items: {
        type: 'object', additionalProperties: false,
        required: ['component', 'file', 'status', 'note'],
        properties: {
          component: { type: 'string' },
          file: { type: 'string', description: 'path:line of the main file' },
          status: { type: 'string', enum: ['solid', 'partial', 'stub', 'orphaned', 'risky'] },
          note: { type: 'string' },
        },
      },
    },
    bugs: {
      type: 'array', description: 'Concrete defects found in code. Each MUST cite file:line and evidence.',
      items: {
        type: 'object', additionalProperties: false,
        required: ['title', 'severity', 'file', 'evidence', 'impact', 'fix'],
        properties: {
          title: { type: 'string' },
          severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low'] },
          file: { type: 'string', description: 'path:line' },
          evidence: { type: 'string', description: 'the actual code/behavior proving the bug' },
          impact: { type: 'string' },
          fix: { type: 'string', description: 'concrete suggested fix' },
        },
      },
    },
    recommendations: {
      type: 'array',
      items: {
        type: 'object', additionalProperties: false,
        required: ['title', 'rationale', 'effort'],
        properties: { title: { type: 'string' }, rationale: { type: 'string' }, effort: { type: 'string', enum: ['S', 'M', 'L'] } },
      },
    },
    missingFeatures: {
      type: 'array',
      items: {
        type: 'object', additionalProperties: false,
        required: ['title', 'why', 'effort'],
        properties: { title: { type: 'string' }, why: { type: 'string' }, effort: { type: 'string', enum: ['S', 'M', 'L'] } },
      },
    },
  },
}

const VERDICT_SCHEMA = {
  type: 'object', additionalProperties: false,
  required: ['title', 'verdict', 'reasoning', 'correctedSeverity'],
  properties: {
    title: { type: 'string' },
    verdict: { type: 'string', enum: ['confirmed', 'refuted', 'uncertain'] },
    reasoning: { type: 'string', description: 'what reading the actual code showed' },
    correctedSeverity: { type: 'string', enum: ['critical', 'high', 'medium', 'low', 'none'] },
  },
}

const GROUND = `
You are reviewing the Liveolator codebase (a cross-platform .NET 8 + Avalonia DJ+VJ app).
Architecture rule: pure platform-agnostic logic lives in src/Liveolator.Core (unit-tested, no native/UI); native/realtime impls live in binding projects (Liveolator.Audio = BASS, Liveolator.Visuals = Silk.NET/OpenGL, Liveolator.Midi = RtMidi). Everything flows through ONE PerformanceAction dispatcher seam. docs/ is the source of truth; docs/18 (implementation-status) and docs/22 (status-and-roadmap) are the living maps, and docs/24+ are prior system reviews — read the parts relevant to your subsystem.
IMPORTANT: There may be significant UNCOMMITTED in-flight work. Run "git status" and inspect new/modified files in your scope — review the ACTUAL current code, not just docs.
Method: use Grep + Read to read the real code. Every bug MUST cite file:line and quote the evidence. Do not speculate — if you cannot find evidence, do not report it as a bug (put it under recommendations/missingFeatures instead). Focus on YOUR lens; do not re-review other subsystems.
${FOCUS ? `\nFOCUS THIS RUN: the owner is stuck on / specifically wants — "${FOCUS}". Prioritize evidence relevant to this, but still cover your whole lens.` : ''}
`

const DIMENSIONS = [
  { key: 'dsp-audio-engine', persona: 'Senior DSP / audio-engine engineer',
    scope: `DSP correctness & real-time safety. Scope: Core/Dsp (FFT, windows, LinearResampler, MasterLimiter, biquad design), Core/Mixer (MixerMath, BiquadCoefficients, CueMixMath, crossfader curves), Core/Analysis (offline BPM/chroma/key/cues), Audio/Playback (TwoDeckBassEngine, BassMixerChannel/StatefulBiquad, BassMixerBackend, MasterAudioSource). Judge: filter coefficient correctness/stability, per-channel biquad state, ZERO allocation/locking on the audio callback thread, resampling quality, limiter behavior, gain staging, click-free loop/seek.` },
  { key: 'beat-sync-clock', persona: 'DSP engineer specializing in tempo/sync (Ableton-Link-style)',
    scope: `The shared beat clock and two-deck sync. Scope: Core/Beat (BeatTimeline, BeatClockState, AudioBeatClock, ManualBeatClock, Quantize/BeatQuantizer/QuantizedLaunch, DeckDrivenBeatClock, MasterClockBridge, SwitchingBeatClock), Core/Audio/Sync (PhaseAlignmentCalculator, PhaseLockController/Correction/Settings, SyncLockState, ISyncCorrectionDriver, TempoSyncCalculator), FirstBeatEstimator. Judge: leader/follower correctness, phase-lock math (±5% cap, per-callback delta cap, graduated correction), host-time<->beat bijection, internal-clock fallback, half/double fold, quantize gating, glitch-free clock switching, thread-safety of the source multiplexer.` },
  { key: 'decks-mixer-transport', persona: 'Professional touring DJ + DJ-software product expert',
    scope: `DJ workflow correctness & feel. Scope: Core/Audio (DeckActionHandler, IMultiDeckPlaybackEngine, SingleDeckEngineAdapter, CueButtonResolver, BeatLoopCalculator, HotCuePositionMapper), Core/Mixer (MixerActionHandler), Audio deck transport. Judge against real DJ vocabulary: one-button sync/beatmatch, cue (temp cue, CDJ back-to-cue, cue-play hold), hot cues (persistence + play-on-jump), loops (beat-accurate, halve/double), pitch/keylock, PFL audibility, end-of-track, EQ kill, gain. Call out what would embarrass us in front of a working DJ.` },
  { key: 'visuals-vj', persona: 'VJ / real-time graphics engineer (Resolume-class)',
    scope: `The VJ engine. Scope: Core/Visuals (scene/layer/bank/macro model, VisualActionHandler, MacroTarget, EffectRef registry), Visuals/Gl (GlVisualPerformanceEngine, LayeredQuadRenderer, FrameUniforms, SceneComposition, BlendModeGl, LiveClockSelector, SkiaImageLoader). Judge: how much of the scene/effect/macro vocabulary actually RENDERS vs is a logged no-op, whether the running render loop re-reads scene state, viewport/resize + macOS Retina, multi-layer blend, beat-reactivity off the shared clock, effect-chain execution, video/camera gaps, the missing authoring UI.` },
  { key: 'midi-controller', persona: 'Hardware controller integration engineer (Ableton control-surface model)',
    scope: `MIDI I/O and the controller-surface plan. Scope: Core/Mapping (ControllerBinding, ControllerMappingProfile, ControlValueConverter, BindingMatcher, ControllerMapper, MidiLearnSession, MidiControllerRouter, MidiFeedbackPublisher, MidiInputPipeline, MidiControlSession, CmdStudio2AProfile), Liveolator.Midi (RtMidi). Judge: value conversion (relative encodings, 14-bit pitch), whether relative deltas are honored end-to-end, feedback/LED path, graceful degradation, soft-takeover absence, the gap to the docs/22 Track-D control-surface (mode-aware, context-following) target, Push 1 profile absence.` },
  { key: 'library-analysis-mcp', persona: 'Music-library & metadata systems engineer',
    scope: `Catalog, analysis, persistence, online enrichment, MCP. Scope: Core/Library (incremental scan, MediaLibrary, TrackQuery/Sort/Facets, ITrackMetadataReader, visual catalog), Core/Analysis, Core/Playlist (HarmonicSetBuilder, LivePlaylist), Liveolator.Media (JsonCatalogStore, SqliteCatalogStore, JsonHotCueStore, JsonLiveSetStore, LiveProfileStore), Liveolator.Online, Liveolator.Mcp. Judge: scan failure isolation, save serialization/atomicity, multiple-decode-pass inefficiency, cache keying (path+mtime+ANALYZER VERSION), manual-edit-lock flags, canonical-key vs derived notations, harmonic correctness, orphaned stores, MCP thin-adapter rule.` },
  { key: 'ui-ux', persona: 'Senior DJ-software UX designer + Avalonia engineer',
    scope: `The Avalonia UI & UX. Scope: Liveolator.App (Features/Live + Modules: DeckView, MixerView, SceneGrid; Features/Libraries; Features/VisualLibrary; Features/Settings; Shell; Theme/Spartan.axaml; Controls Knob/Fader/WaveformStrip; ViewModels). Judge: feedback-driven binding correctness (no loops), controls wired in the VM but unbound in the view, disabled/labeled controls with no backend, undefined style classes, waveform rendering, accessibility (keyboard/focus/contrast per docs/19), reachability of backend features, gig-readiness. Cite axaml/VM file:line.` },
  { key: 'architecture', persona: 'Principal software architect',
    scope: `Architecture & layering. Scope: the dispatcher seam (Core/Actions: PerformanceAction, PerformanceActionKind, dispatcher, handlers), App/Composition/ServiceConfig.cs, Core purity, seam consistency (immutable-record-state / interface-behavior, injected IHostClock). Judge: handler ownership/duplication AND zero-ownership (action kinds no handler claims), action payload limitations, orphaned services not in DI, god-methods, circular deps, "state out / actions in" separation, and the lifetime ownership of the realtime clock/sync pump.` },
  { key: 'testing-build-ci', persona: 'QA lead + build/release engineer',
    scope: `Test coverage, build health, CI, native distribution. Scope: all tests/ projects, the .sln/.csproj, scripts/ (fetch-bass.ps1 vs .sh), CopyBassNative target, any CI config (.github/workflows etc.), docs/14. Judge: whether new/in-flight code has committed tests (TDD-first), the "verified manually not in CI" gap, divergence between the two native-fetch scripts, whether required native libs are verified at build, flaky/existence-only tests, un-vendored native artifacts blocking a shippable build, and absence of CI.` },
  { key: 'product-design-roadmap', persona: 'DJ-gear product manager + modern music-equipment design expert',
    scope: `Product strategy, competitive position, roadmap. Read docs/00, 15, 20, 21-followup, 22, 23, 24 and the actual feature set. Judge: the differentiator (one shared audio<->visual beat clock), gaps vs Rekordbox/Serato/Traktor/Resolume, what a credible v1 needs, the hardware story (Push 1 + CMD STUDIO 2A), packaging/licensing/distribution readiness, and whether the current track ordering is still right given in-flight work. Produce FEWER bugs and MORE strategic recommendations/missing-features. Keep your map to ~5 high-level entries.` },
]

phase('Review')
log(`Dispatching ${DIMENSIONS.length} expert reviewers across the Liveolator system…${FOCUS ? ` Focus: "${FOCUS}".` : ''}`)

const results = await pipeline(
  DIMENSIONS,
  (d) => agent(
    `${GROUND}\n\nYOUR ROLE: ${d.persona}.\nYOUR SUBSYSTEM: ${d.key}.\nSCOPE & WHAT TO JUDGE:\n${d.scope}\n\nProduce a professional review: a map of what exists (with status), concrete bugs (file:line + evidence — be rigorous, no speculation), recommendations, and missing features. Be specific and cite code.`,
    { label: `review:${d.key}`, phase: 'Review', schema: FINDINGS_SCHEMA }
  ),
  (review, d) => {
    if (!review) return null
    const serious = (review.bugs || []).filter((b) => b.severity === 'critical' || b.severity === 'high')
    if (serious.length === 0) return { review, verifiedBugs: [] }
    return parallel(serious.map((b) => () =>
      agent(
        `${GROUND}\n\nYou are an adversarial verifier. A ${d.persona} claims this bug exists in the ${d.key} subsystem:\n\nTITLE: ${b.title}\nSEVERITY: ${b.severity}\nFILE: ${b.file}\nEVIDENCE CLAIMED: ${b.evidence}\nIMPACT: ${b.impact}\n\nOpen the cited file and surrounding code. Try to REFUTE the claim. Confirm ONLY if the code genuinely exhibits the defect. Default to 'refuted' or 'uncertain' if the evidence does not hold up or the code already handles it. Set correctedSeverity to your honest assessment ('none' if refuted).`,
        { label: `verify:${d.key}:${b.title.slice(0, 30)}`, phase: 'Verify', schema: VERDICT_SCHEMA, model: 'sonnet' }
      ).then((v) => ({ ...b, verdict: v }))
    )).then((vbs) => ({ review, verifiedBugs: vbs.filter(Boolean) }))
  }
)

const clean = results.filter(Boolean)
return {
  focus: FOCUS,
  reviewerCount: clean.length,
  reviews: clean.map((r) => ({
    subsystem: r.review.subsystem,
    persona: r.review.persona,
    summary: r.review.summary,
    map: r.review.map,
    bugs: r.review.bugs,
    verifiedBugs: r.verifiedBugs,
    recommendations: r.review.recommendations,
    missingFeatures: r.review.missingFeatures,
  })),
}
