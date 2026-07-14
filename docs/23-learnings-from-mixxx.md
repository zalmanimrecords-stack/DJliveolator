# 23 — Learnings from Mixxx

> **What this is:** a study of [Mixxx](https://github.com/mixxxdj/mixxx) (the most mature
> open-source DJ application) and what its architecture teaches us for Liveolator. Mixxx is
> C++/Qt; we are .NET 8/Avalonia. We borrow **designs, algorithms, and hard-won lessons** —
> never code (see the licensing wall below). Each section maps a Mixxx subsystem to the
> Liveolator doc/skill it informs.

---

## ⚠️ Licensing wall — read before touching Mixxx source

Mixxx is **GPLv2**. Liveolator is distributed to users and uses a **commercial BASS license**
(decided 2026-06-05, see `CLAUDE.md`) — i.e. we are explicitly a closed/proprietary
distribution. GPLv2 is copyleft: **copying or close-paraphrasing Mixxx source into Liveolator
would force the whole app to become GPLv2**, which conflicts with our distribution model.

Rules for using this study:

- ✅ **Allowed:** read Mixxx for *understanding*; reimplement well-known DSP algorithms from
  their *papers/specs* (the math isn't copyrightable); copy *architectural patterns* (a
  control registry, a leader/follower sync model — ideas, not expression).
- ✅ **Allowed:** read the user manual, wiki, and DeepWiki — these are docs, not code.
- ❌ **Forbidden:** pasting/translating Mixxx `.cpp/.h` into our tree; "porting" a Mixxx file
  line-by-line; vendoring `qm-dsp` or any GPL lib into a shipped build.
- ⚠️ **qm-dsp** (Mixxx's beat/key engine) is **GPLv2** too. Our BPM detector is already a clean
  C# reimplementation (`Core/Analysis/`, ported from Zalmanolator) — keep it that way. Do not
  link qm-dsp.

When in doubt: learn the *idea* here, then implement from first principles in C#.

---

## The one lesson that matters most: the control registry == our action seam

Mixxx's entire architecture hangs on **`ControlObject`** — a global registry of named,
controllable values. Hardware (MIDI), the UI (widgets via `ControlProxy`), scripting, and the
audio engine **all read/write the same control objects**; none of them call each other directly.
Updates propagate bidirectionally: a knob move and an engine state change both flow through the
same control.

**This is exactly Liveolator's guiding principle** (`docs/README.md`, `docs/04`): hardware/UI/
autopilot never touch engines directly — everything flows through `PerformanceAction` → dispatcher
→ engine. Mixxx is independent validation that the seam architecture is the right call for a
DJ+VJ app at scale.

Two refinements Mixxx has that we should consciously decide on:

1. **Bidirectional by default.** Mixxx controls push *engine→UI* (and engine→LED) as naturally as
   *UI→engine*. Our actions are command-shaped (intent flowing *in*). We already publish engine
   state out (e.g. `BeatClockState`, BPM display). Keep these two directions cleanly separated:
   **`PerformanceAction` in, observable state out** — don't let them collapse into one
   read/write bus, or we lose the serializable-intent property that makes autopilot + MIDI-learn
   uniform.
2. **Named-control addressability.** Mixxx can address any control by string
   (`[Channel1],volume`). Our MCP layer (doc 17) and MIDI-learn (doc 05) want similar stable
   addressing of action targets. Worth ensuring our action kinds + targets have stable string
   identities for learn/automation/agents.

---

## Track analysis pipeline → informs `docs/16`, `docs/13`

Mixxx runs all offline analysis through a **uniform analyzer interface**: every analyzer
implements `shouldAnalyze()` / `processSamples()` / `storeResults()`. The library scanner moves
to its **own thread**; audio is decoded **incrementally in chunks** and fed to all analyzers in
one pass; a configurable **thread pool** runs files in parallel.

Analyzers (each one responsibility — matches our global standard #3):

- `AnalyzerSilence` — intro/outro via −60 dB threshold
- `AnalyzerBeats` — BPM + beat grid (pluggable: Queen Mary default, SoundTouch alt)
- `AnalyzerKey` — musical key
- `AnalyzerWaveform` — visual summary data
- `AnalyzerReplayGain` — loudness normalization

**Persistence is layered:** `TrackDAO` (BPM/key/gain), `AnalysisDAO` (waveform summaries),
`CueDAO` (intro/outro/cue markers). **Conditional re-analysis:** each analyzer checks existing
results + file mtime before reprocessing; a **BPM-lock flag** prevents auto-overwrite of manual
edits; beat-grid edits keep an **undo stack** (up to 10).

**Lessons for Liveolator:**

- One `IAnalyzer` interface with `ShouldAnalyze / ProcessSamples / StoreResults`, **single decode
  pass feeding all analyzers** (we currently risk separate passes per feature — decode once).
- **Background thread pool**, never on UI thread; incremental chunked decode for big files
  (aligns with `Audio` offline-decode design).
- **Cache keyed on path + mtime**; never recompute valid results. Our JSON catalog cache (doc 13)
  should store an analysis-version + source-mtime per track.
- **Respect manual overrides:** a lock flag on BPM/key so re-scan never clobbers a human edit.
  This is a correctness-and-trust feature, not a nicety.

---

## Beat detection (qm-dsp) → informs `docs/03`, `Core/Analysis`

Mixxx's default beat/BPM and key detection are the **Queen Mary DSP** Vamp plugins. The beat
tracker produces beat pulses; downbeat indices come from QM; a tempogram (2-D periodicity of an
onset-detection function) supports meter/variable-tempo handling. They **filter and smooth raw
per-window tempos** to detect significant tempo changes (constant vs variable tempo).

**Lessons (algorithmic — reimplement, don't link qm-dsp):**

- Our planned `OnsetDetectionEngine → TempoEstimator → BeatTracker` (doc 03) matches the
  standard pipeline. The QM approach confirms: **onset-detection function → periodicity estimate
  (tempogram/autocorrelation) → phase-locked beat tracking**.
- **Smooth + threshold tempo over a rolling window** to decide *constant vs variable* tempo
  rather than chasing every frame — this is the difference between a jittery and a stable clock.
- Keep **half/double-time candidates** explicit (we already plan this) — QM's known weak spot is
  octave errors, and DJ tracks make them common.

---

## Sync Lock / master clock → directly informs `docs/03` beat engine + `IBeatTimeline`

Mixxx's `EngineSync` is the closest external analog to our shared beat clock. Model:

- **Leader/follower.** One `Syncable` is leader; others follow. Two leader types:
  `LeaderExplicit` (user-pinned, sticky until track stops/ejects) and `LeaderSoft` (auto-chosen,
  reassignable). `EngineSync` propagates the **leader's BPM and beat-distance** to followers.
- **Two BPMs:** *base BPM* (at rate 1.0) and *effective BPM* (base × rate slider). Followers
  match effective BPM and then nudge phase.
- **Beat distance** = fractional progress through a beat `[0.0, 1.0)`; `0.0` = on the beat.
  Followers receive the leader's beat distance and correct via their rate slider.
- **Phase correction is graduated and capped:** error `<0.01` → no change; `[0.01, 0.2)` →
  proportional correction **capped at ±5% rate**, with a **per-callback delta cap** so corrections
  never sound jerky. Correction runs **only when quantize is on**.
- **Fallback `InternalClock`:** a synthetic syncable that holds tempo when no deck plays;
  `pickLeader()` priority = explicit → playing+audible → InternalClock. Its BPM persists.
- **No notification loops:** writing a parameter must not re-fire its own change callback.

**Lessons for Liveolator:**

- Our `IBeatTimeline` is the natural home for an **`InternalClock`-equivalent**: a stable
  tempo reference that exists even when nothing is playing, so visuals/autopilot never lose the
  beat. We should make this explicit.
- When we add **deck↔deck sync** (DJ engine, doc 11), adopt the **leader/follower model with an
  explicit/soft distinction** and **graduated, rate-capped phase correction gated on quantize** —
  this is the proven recipe for artifact-free beatmatch.
- Separate **base vs effective BPM** in our state so a rate/tempo change and a detected-tempo
  change don't fight.
- Guard against notification loops in our state publishing (relevant now that BPM display + beat
  clock both observe and emit).

---

## Audio engine: EngineMixer / EngineBuffer / EngineChannel → informs `docs/01`, `docs/02`, `docs/11`

Real-time chain runs on the audio callback thread:

- **`EngineBuffer`** (one per deck) holds almost all player logic: decode, resample, loops,
  hotcues, sync. Stem/multichannel mixed down to stereo here.
- **`EngineChannel` / `EngineDeck`:** decks and samplers are *the same thing* to the mixer. An
  **`orientation` control** decides which crossfader side a channel feeds — so the mixer doesn't
  special-case decks vs samplers vs mic.
- **`EngineMixer`** processing order: mix crossfader-orientation buses → main mix → main-channel
  effects → talkover/mic ducking → headphone (PFL) path with post-fader effects + optional main
  contribution → booth output → main gain → balance.

**Lessons for Liveolator:**

- **One channel abstraction** for everything that produces audio (deck A, deck B, future sampler/
  cue player), with an **orientation/route property** rather than per-source branching in the
  mixer. Keeps our software mixer (crossfader + per-channel EQ/filter, doc 11) simple and
  uniform.
- **Headphone-cue (PFL) is a first-class path**, processed separately and *before* main gain —
  the CMD STUDIO 2A's built-in cue output should map to this. Bake the PFL split into the mixer
  graph from the start, not as an afterthought.
- Keep **all per-deck playback logic behind one component** (our `IBassPlayback` already isolates
  BASS) — that's our `EngineBuffer` equivalent. Loops/hotcues/sync belong there, behind the seam.
- BASS gives us decode/resample/mix/tempo natively, so we don't rebuild `EngineBuffer` internals —
  but the **mixer-graph topology** (orientation buses → main → effects → PFL → gain) is the part
  worth copying as a design.

---

## Waveform rendering → informs `docs/12` UI + `docs/08` visuals

Mixxx 2.4 rewrote waveforms for the GPU and documented the lessons publicly:

- **Precompute a waveform *summary*** at analysis time (stored via `AnalysisDAO`), then render the
  summary — don't re-scan PCM per frame.
- **All waveform types are GLSL-shader rendered** (Simple/Filtered/HSV/RGB/RGB-L-R). The legacy
  `QPainter` + fixed-function GL combo was the bottleneck, **especially on macOS**.
- They replaced `QGLWidget` with a `QOpenGLWindow` inside a `QWidget` via `createWindowContainer`.
- **Sync the scroll animation to the display refresh** — a periodic timer by default, and a
  **phase-locked-loop (PLL)** mode that tracks the real refresh rate/timing (default on macOS) to
  kill jitter/frame drops. Result: smooth 60 fps, lower CPU.

**Lessons for Liveolator (we already render via Silk.NET/OpenGL):**

- **Precompute waveform summary data in the analysis pipeline** (doc 16) and store it (doc 13) —
  the scrolling deck waveform reads the summary, never the PCM.
- **Render waveforms with GLSL shaders**, not CPU drawing — and we get this nearly free since our
  compositor is already OpenGL (doc 08). Consider a shared shader path for waveforms + visual
  layers.
- **macOS is where naive rendering dies.** Since Mac is a hard requirement, validate waveform +
  visual frame timing on Mac early. Plan for **refresh-synced rendering** (and keep PLL-style
  refresh tracking in mind if we see jitter).

---

## Controller mapping → ❌ NOT taken from Mixxx — owner decision: follow the **Ableton control-surface model**

> **Decision (2026-06-06, owner):** we do **not** adopt Mixxx's per-control mapping approach for
> controller mapping. Liveolator's controller layer is built on the **Ableton "Control Surface"
> model** instead. This section records *why*, so future work doesn't drift back to the Mixxx
> style. The roadmap item lives in `docs/22` **Track D**.

**How Mixxx does it (the approach we are *not* taking):** a two-layer scheme — a static **XML
mapping file** binding raw MIDI (note/CC/program) → a named `ControlObject`, plus **JavaScript**
handlers for stateful controls (jog scratch, shift). Each control is mapped individually; the
controller is a flat set of bindings. Soft-takeover and LED feedback are built in, but the surface
has no inherent notion of *mode* or *what is selected*.

**How Ableton/Push does it (the model we adopt)** — a cohesive, device-aware **Control Surface**,
not knob-by-knob mapping:

- **Device-aware surface, not per-control bindings.** A device (Push 1, CMD STUDIO 2A) is a known
  *surface* with a holistic definition of its whole layout — not a pile of learned CC→action rows.
- **Mode-aware.** The same physical encoders/pads mean different things depending on the active
  mode/view. Per the Ableton docs, Push's "eight encoders are used for a variety of things
  depending on what mode or view is in focus." One surface, many context-dependent layers.
- **Context-following.** Encoders map automatically to the **currently selected** target's
  parameters (in Live: the selected device; for us: the focused deck / visual layer / effect),
  and pads follow the current bank/scene/track — the surface tracks the selection rather than
  being statically bound to fixed targets.
- **Holistic bidirectional feedback.** The LCD + RGB pads + button LEDs reflect engine state as a
  unified display (parameter names/values, pad colors) — feedback is a property of the surface,
  not a per-binding afterthought.
- **Manual-override escape hatch.** Live still allows custom per-control MIDI mappings, reached by
  deactivating the built-in surface (Push **User Mode**). We keep the equivalent: an optional
  manual MIDI-map layer for power users / unsupported devices, *on top of* the surface model.

**What this means for Liveolator (supersedes the MIDI-learn framing in `docs/05`):**

- Build a **Control Surface abstraction** per supported device — a cohesive, mode-aware,
  context-following object — rather than a flat list of learned `ControllerBinding`s. The surface
  still emits `PerformanceAction`s into the dispatcher seam (architecture unchanged); only the
  *mapping model above the seam* changes from "learn one CC" to "device-aware surface."
- **Soft-takeover** stays — still required for absolute knobs/faders (CMD STUDIO 2A EQ/filter,
  Push encoders) so values don't jump on mode/selection change. It fits the surface model cleanly.
- **Feedback (LED/LCD) is first-class and holistic**, driven from **observable engine state** (the
  same state the UI binds to) — reinforcing the "state out, actions in" separation. Push 1 LED
  model (NoteOn velocity=color, CC buttons, SysEx LCD — doc 06) is the output side of the surface.
- Keep a **manual MIDI-map override layer** as the User-Mode-style escape hatch for stateful or
  unsupported controls — but the primary path is the device-aware surface, not learn-per-control.

*(Note: `docs/05` and the `add-controller-mapping` skill still describe the MIDI-learn approach and
must be revised to the control-surface model when Track D is scheduled.)*

---

## Effects framework → informs `docs/08` visuals + future audio FX

Mixxx's effects design is unusually clean and maps well to our **GLSL visual macro** model:

- **EffectChain** = ordered list of effects applied sequentially; loaded into **Effect Units**
  that attach to decks/master/headphone/mic.
- **`EffectManifest`** describes an effect + its parameters declaratively (default, min/max,
  control hint: knob/toggle/slider). Instances hold the live values.
- **Metaknob ("wonder knob"):** one knob per chain that **links to many parameters at once**, with
  per-parameter linear/log scaling and link modes (None / Linked / Linked-Left / Linked-Right,
  with invert). One gesture, many parameters.
- **Backend abstraction:** native/LADSPA/LV2/VST all behind one interface (mirrors their MIDI
  backend model).
- **Wet/dry proportional mix**; disabled == wet 0.

**Lessons for Liveolator:**

- Our **visual layer effect chain** (doc 08) is conceptually Mixxx's EffectChain — an ordered
  list of GLSL effects per layer. Confirm the model: **layer → ordered effect chain → composite**.
- Our **`VisualMacro` / `MacroTarget`** (skill `add-visual-effect`) *is* the metaknob: one Push
  knob → many shader uniforms with per-target scaling. Mixxx validates this and adds detail worth
  copying: **per-parameter scaling curves (linear/log) and invert**, and **link modes** for
  stereo-like paired params.
- A **declarative effect manifest** (parameter name, range, default, UI hint) per GLSL effect lets
  UI, Push knobs, MCP agents, and automation all discover and drive parameters uniformly — the
  same payoff Mixxx gets. Strongly worth adopting for the visual engine.
- **Wet/dry per effect** as a standard parameter — cheap, expected by performers.

---

## Key detection & harmonic mixing → informs `docs/16`, `docs/17` MCP, skill `add-playlist-rule`

- Key detection is the **QM-DSP key analyzer** (reimplement the algorithm, don't link the lib).
- Mixxx stores a **standard key** and converts to display notations via a **lookup table** —
  supports **Open Key / Camelot** (Camelot wheel is public-domain notation) and **user-defined**
  notations.
- Harmonic mixing = compatibility computed off the Camelot mapping.

**Lessons for Liveolator:**

- Store the **canonical key once**, derive Camelot/Open-Key/etc. via a **pure lookup/conversion**
  — don't store display strings. Our harmonic MCP tools (`compatible_keys`, `harmonic_matches`,
  `build_harmonic_playlist`) should compute off the canonical key + a Camelot table, exactly like
  Mixxx. (Confirm our current `Core` harmonic logic follows this — single source of truth for key.)
- Camelot notation is safe to use (public domain). Good for our UI + agent outputs.
- Offer **notation choice** (Camelot vs traditional) as a display concern only, never in the
  stored model.

---

## Summary: what to borrow, what to skip

**Borrow (designs/algorithms, reimplemented in C#):**

- Uniform **analyzer interface + single-decode-pass + thread pool + mtime-keyed cache + manual-lock
  flags** for the analysis/library pipeline.
- **Leader/follower sync** with explicit/soft leaders, base-vs-effective BPM, **graduated
  rate-capped phase correction gated on quantize**, and an **always-on internal clock**.
- **One channel abstraction with an orientation/route**, and a **first-class PFL/headphone path**
  in the mixer graph.
- **Precomputed waveform summaries + GLSL waveform rendering + refresh-synced animation** (validate
  on macOS early).
- **Declarative effect manifests + metaknob (multi-param link with scaling/invert)** for the visual
  effect chain.
- **Canonical key stored once, Camelot derived by table** for harmonic mixing.

**Explicitly NOT from Mixxx (owner decision):**

- 🎛️ **Controller mapping follows the Ableton control-surface model**, not Mixxx's per-control
  mapping. We keep the *general* lessons that happen to agree with Ableton — **soft-takeover** and
  **state-driven holistic LED/LCD feedback** — but the mapping *model* is device-aware surfaces,
  not learn-one-CC bindings. See the controller section above and `docs/22` **Track D**.

**Skip / avoid:**

- ❌ Any Mixxx/qm-dsp **source code or line-by-line port** (GPLv2 — would infect our distribution).
- ❌ Linking **qm-dsp / LADSPA / LV2** GPL libs into a shipped build.
- ❌ Mixxx's **per-control XML/learn mapping** workflow — superseded by the Ableton surface model.
- ❌ Qt-specific rendering plumbing (`QOpenGLWindow`/`createWindowContainer`) — we have Avalonia +
  Silk.NET; take the *lesson* (GPU + refresh sync), not the mechanism.

---

## Sources

- [Mixxx repository (GPLv2)](https://github.com/mixxxdj/mixxx)
- [Track Analysis Pipeline — DeepWiki](https://deepwiki.com/mixxxdj/mixxx/6.3-track-analysis-pipeline)
- [Developer Guide: Engine — Mixxx Wiki](https://github.com/mixxxdj/mixxx/wiki/Developer-Guide-Engine)
- [Developer Guide: SyncLock — Mixxx Wiki](https://github.com/mixxxdj/mixxx/wiki/Developer-Guide-SyncLock)
- [Improved Scrolling Waveforms in Mixxx 2.4](https://mixxx.org/news/2024-02-23-improved-waveforms/)
- [Effects Framework — Mixxx Wiki](https://github.com/mixxxdj/mixxx/wiki/Effects-Framework)
- [MIDI scripting — Mixxx Wiki](https://github.com/mixxxdj/mixxx/wiki/midi_scripting)
- [Beat Detection — Mixxx User Manual](https://manual.mixxx.org/2.3/en/chapters/preferences/beat_detection)
- [Key Detection — Mixxx User Manual](https://manual.mixxx.org/2.3/en/chapters/preferences/key_detection)
- [Downbeats and Phrase Detection — Mixxx Wiki](https://github.com/mixxxdj/mixxx/wiki/Downbeats-And-Phrase-Detection)
</content>
</invoke>
