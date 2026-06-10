# 27 — System Review, Bug Map & Forward Plan (multi-expert)

> **Purpose:** the full-system review conducted as a panel of ten domain experts (senior DSP
> engineer, tempo/sync specialist, professional touring DJ, VJ/real-time graphics engineer,
> hardware-controller engineer, music-library/metadata engineer, DJ-software UX designer, principal
> software architect, QA/release engineer, DJ-gear product manager). Each expert read the **actual
> current code** (including the uncommitted in-flight VisualStage Start/Show work) and every
> High/Critical bug was **adversarially re-verified** against the source before being recorded here.
> Date: **2026-06-10**. Supersedes [doc 24](24-system-review-2026-06-07.md) for the next wave.
>
> **Baseline measured for this review (ground truth, not memory):**
> - Solution **builds clean (exit 0)**.
> - Local test suite is **RED**: **10 failing tests on clean HEAD (`1f65908`)**, all in
>   `LibrariesViewModel*Tests`; **11 failing on the in-flight working tree** (run-order-dependent — see
>   B0). Confirmed by running the suite on both the working tree and an isolated HEAD worktree. Root
>   cause is a **pre-existing ReactiveUI global-scheduler isolation defect** (documented in `docs/18`),
>   not a product regression; CI is reported green.
> - Per-project pass/fail/skip (no-build run): **Core 781/0/0 · Audio 181/0/0 · Media 101/0/0 ·
>   Visuals 95/0/0 · MIDI 27/0/0 · Online 23/0/0 · Integration 25/0/0 · App 300/11/0** (working tree;
>   301/10/0 on clean HEAD). Eight of nine projects are fully green; **only `Liveolator.App.Tests` is
>   red, and every failure is in one namespace** — `Libraries`.
>
> Where this doc and `docs/18`/`docs/22` disagree on a code fact, **this doc wins** — it was measured
> against the working tree on 2026-06-10. `docs/18` remains the living module map.

---

## 0. Headline verdict

The architecture and discipline remain genuinely strong, and the last wave **closed five of doc 24's
headline holes** (see §0.1). The DSP/sync **math** is correct and deeply tested; the dispatcher seam,
Core purity, and tolerant persistence are exemplary. This is a credible, working DJ+VJ app — not a
sketch — and the README that still calls it an "early design phase" is wrong (B-MED, §2.2).

Two things stand out from this review:

1. **The local test suite is red, and the panel could not see it.** The ten reviewers read code; they
   do not run tests, so none reported that `Liveolator.App.Tests` has **10 failures on HEAD**, all in
   `LibrariesViewModel*Tests`. Root cause (already noted in `docs/18`) is a **pre-existing ReactiveUI
   global-scheduler isolation defect** — three test classes mutate the static `RxApp.*Scheduler`, so
   failures are run-order-dependent (10 vs 11 across trees), and "CI green" is itself ordering-fragile.
   It is the single highest-ROI thing to fix: a suite that's red on dev machines voids the
   "validate before finishing" backstop everything else relies on. (Not a product bug in the VM.)
2. **The remaining gaps cluster into three themes**, same shape as doc 24 but with new specifics:
   - **"Wired but not effective / not reachable."** Per-layer opacity already has a live per-frame
     uniform, yet dragging the knob forces a full compositor teardown (re-decode every image,
     recompile every shader) every value change. Beat-synced transitions, layer toggles and the
     VU-meter toggle are implemented in the VM but not bound in the view. The online key lookup
     populates a field nobody reads. These are small fixes that turn theatre into function.
   - **"The differentiator's correctness edges."** Phase alignment mixes beat-fraction units across
     half/double-tempo pairs (a 70-vs-140 set can lock to the off-beat); the sync re-snap seeks to a
     latency-shifted position now that production latency is non-zero; the live EQ coefficient swap is
     a torn read on the audio thread.
   - **"The promised-on-the-box experience is unproven."** No keylock, no audible PFL/multi-channel
     cue, no captured Push 1 / CMD STUDIO 2A profile, no VJ authoring UI, and **no packaging/
     distribution path** — which is the project's hard reason to exist.

### 0.1 What changed since doc 24 (now CLOSED)

Verified against current code during this review:

- **doc 24 B1** (shared clock mis-scaled by master pitch) — **closed**; continuous beat now computed
  from base BPM (`TwoDeckBassEngine.cs:541`).
- **doc 24 B3** (sync pump on the UI thread) — **closed**; a dedicated high-priority `MasterClockPump`
  thread drives correction.
- **doc 24 B5** (GL render loop never re-reads the scene) — **closed**; composition-version dirty flag
  at `GlVisualPerformanceEngine.cs:361`. (This fix introduced the new opacity-rebuild bug, B-VIS-1.)
- **doc 24 B10/B11** (no CI; `fetch-bass.sh` omits BASSFLAC) — **closed**; `.github/workflows/ci.yml`
  windows+macos matrix exists, and both fetch scripts now consume a shared `bass-libraries.manifest`.
- **doc 24 medium** (orphan `AutoMix*`/`Transport*` action kinds) — **pruned**.

---

## 1. System map (by subsystem)

Status legend: **solid** (built + tested + effective) · **partial** (works but with a named limit) ·
**stub** (logged no-op / placeholder) · **risky** (a defect or robustness hole below).

| Subsystem | Verdict | Highlights | Worst issue |
|-----------|---------|-----------|-------------|
| **DSP / audio engine** | solid | RBJ-cookbook EQ/filter correct & a0-normalized; allocation-free true-peak look-ahead limiter; RT path audited for allocations; clean pure/native boundary | **Live biquad coefficient swap is a torn read on the audio thread (HIGH)**; unnormalized tempo autocorrelation biases BPM up (MED); symmetric/periodic Hann mislabeled (LOW) |
| **Beat / sync / shared clock** | solid→partial | Single immutable Link-style timeline; memoryless proportional phase-lock with hard clamp + re-snap; dedicated pump thread; well unit-tested | **Phase alignment mixes beat-fraction units across half/double tempos (HIGH)**; **re-snap seeks to a latency-shifted position (MED, now live)**; no per-tick rate-slew limiter |
| **Decks / mixer / transport** | partial | Slot-addressed engine, one-button SYNC, sample-accurate loops, persistent hot cues, equal-power PFL math | **No keylock / master-tempo — pitch always follows tempo (HIGH)**; **beat loops not grid-snapped even when Quantize armed (HIGH)**; BpmNudge silently dropped on a synced deck (MED) |
| **Visuals / VJ** | partial | Pure scene/layer/bank/macro model; multi-layer GL compositor renders image + generator layers with blend/opacity + beat flash + live audio uniforms | **Opacity knob forces a full compositor rebuild per value change (HIGH)**; SetLayerSource/ToggleLayer rebuild the whole stack (MED); LaunchClip/Strobe/Transition are no-ops; no video/camera; no authoring UI |
| **MIDI / controller** | partial (solid spine) | Library-agnostic mapping, correct 14-bit/relative decode, graceful degradation, verified 0-based channel map | UI-learn drops relative tick scaling → ~128× too sensitive (MED); learn can't set relative encoding (MED); no soft-takeover; no Push 1 profile/SysEx; no release edge for momentary CC |
| **Library / analysis / MCP** | solid | Failure-isolated incremental scan, atomic versioned persistence, resumable background re-analysis, correct Camelot logic, thin MCP adapters | **Re-scan of a Modified file destroys manual beat-grid/BPM edits (HIGH)**; **online key lookup never applied — KeyName populated, only Camelot read (HIGH)**; `JsonFileSnapshotIo` save race (MED) |
| **UI / UX (Avalonia)** | partial | Every control is an action source; loop-safe feedback binding; gig-usable DJ side; well-built custom Knob/Fader/WaveformStrip | **Global Tab handler breaks keyboard focus traversal app-wide (HIGH)**; live VisualControl commands unreachable from the view (MED); waveform bar-downbeat lines drawn in wave color, not blue accent (MED) |
| **Architecture & layering** | solid | Immutable serializable actions; fail-fast single-owner dispatcher; Core purity intact; single well-commented composition root | DI `ServiceProvider` never disposed → native singletons leak on exit (MED); tap-tempo/beat actions stop driving visuals when audio is up — dual-clock divergence (MED); `ServiceConfig.Build` is a ~470-line god-method |
| **Testing / build / CI** | partial | 9 test projects, native deps behind fakes/skip guards, headless-clean; CI matrix + shared BASS manifest now exist; in-flight VisualStage work landed TDD-first | **Local suite goes RED (10 `LibrariesViewModel*` failures on HEAD) from a pre-existing ReactiveUI global-scheduler isolation defect — baseline finding**; CopyBassNative never verifies required libs (MED); no packaging/native CI job; UiShots assert nothing; docs/14 still describes the dead projectM/NAudio stack |
| **Product / strategy** | — | Differentiator (one shared audio↔visual clock) is real and recently de-risked; DJ core credible-minimal; library production-grade | Hardware-cue + controller-profile proof unstarted; VJ unauthorable without hand-editing JSON; **packaging/licensing/distribution entirely unstarted** (the hard requirement); README advertises a stale "design phase" (MED) |

---

## 2. Bugs (verified against code)

### 2.1 Critical / High — fix before any "it works" claim

Every entry below was adversarially re-verified; verdict = **confirmed** unless noted. The
test-baseline finding (B0) was measured by running the suite, not by the read-only panel.

| # | Bug | File | Why it matters | Fix |
|---|-----|------|----------------|-----|
| **B0** | **Local test suite goes red: 10 `LibrariesViewModel*Tests` fail on clean HEAD** (11 on the working tree) — a **pre-existing ReactiveUI global-scheduler isolation defect**, already noted in `docs/18`. Three test classes (`*Tests`, `*LiveTests`, `*PersistenceTests`) each set the static `RxApp.MainThreadScheduler`/`TaskpoolScheduler` in their ctor; cross-class interference makes the count run-order-dependent (hence 10 vs 11). CI is reported green. | `tests/Liveolator.App.Tests/Libraries/LibrariesViewModelTests.cs:15` (shared `RxApp` statics) | A suite that's red on dev machines voids the local "validate before finishing" backstop and trains everyone to ignore failures; "CI green" is itself fragile because it depends on runner ordering/parallelization of a mutated global. Not a `LibrariesViewModel` product bug — the VM logic is fine in isolation. | Isolate the ReactiveUI scheduler per test (a fixture/collection that saves+restores `RxApp.*Scheduler`, or run these classes in a non-parallel collection), so the static is not clobbered mid-run. Restore a deterministically-green local suite before landing new work. **Found by running the suite, not by the read-only panel.** |
| **B1** | **Live biquad coefficient swap is a torn read on the audio thread.** `BiquadCoefficients` is a 40-byte (5×double) struct written from the UI/action thread and read in `Process` on the BASS update thread with no `volatile`/lock/`Interlocked`. | `src/Liveolator.Audio/Playback/StatefulBiquad.cs:30` | A knob move can feed the DF1 difference equation a part-old/part-new coefficient set; the transient can place poles outside the unit circle for a block → audible click/zipper. (The sibling `_gain/_peak/_rms` in `BassMixerChannel` are already `volatile`; this one was missed.) | Publish coefficients atomically: hold them in a class swapped by reference via `Volatile.Write`/`Interlocked.Exchange` (a reference assignment is torn-free), or a lock-free double buffer. Add a stress test hammering `SetCoefficients` while `Process` runs. |
| **B2** | **Phase alignment mixes beat-fraction units across half/double-folded tempos** — can lock the follower's downbeat onto the leader's off-beat. Each `BeatDistance` divides by its own deck's beat duration, then the two fractions are subtracted. | `src/Liveolator.Core/Audio/Sync/PhaseAlignmentCalculator.cs:59` | Half/double pairings (70 vs 140 BPM — extremely common) compute the error against the leader's faster grid; tempo matches but beats can sit half a beat out — exactly what the phase loop exists to prevent. Equal-tempo (the only tested case) is unaffected. | Express both distances on one common grid before subtracting (scale the follower's progress by `leaderBpm/followerBpm`, or fold the follower's phase into the leader's beat). Add explicit half/double-tempo tests. |
| **B3** | **Beat loops are not snapped to the grid even when Quantize is armed.** `SetLoop` reads the raw playhead and passes it straight to `BeatLoopCalculator.Region`; neither `_quantize[slot]` nor `_firstBeat[slot]` is consulted. | `src/Liveolator.Audio/Playback/TwoDeckBassEngine.cs:880` | A "4-beat loop" starts wherever the playhead happens to be, so loop boundaries fall off the kick grid and the loop lurches — the precise thing Quantize exists to fix. | When `_quantize[slot]` (or always for beat loops), snap `startSeconds` to the nearest beat via `_firstBeat[slot]`/`_baseBpm[slot]` (reuse `PhaseAlignmentCalculator.BeatDistance`) before `Region`. Add a quantized-loop test. |
| **B4** | **No keylock / master-tempo: pitch always shifts with tempo.** `SetDeckRate` scales `ChannelAttribute.Frequency` (vinyl-style); there is no `BASS_FX` tempo stream, no keylock flag, no `KeyLock` action kind. | `src/Liveolator.Audio/Playback/BassMixerBackend.cs:386` | Beatmatching >~3–4% audibly detunes vocals/melody, and SYNC across different BPMs chipmunks/down-pitches the slave — unacceptable for open-format/harmonic mixing and a baseline Rekordbox/Serato/Traktor feature. (Self-documented in docs/20 & 21.) | Build deck streams through `BASS_FX TempoCreate`; drive `BASS_ATTRIB_TEMPO` when keylock is on (rate via `BASS_ATTRIB_TEMPO_FREQ` off), falling back to the current frequency-scale path otherwise. Expose a `DeckKeylockToggle` action. *(Large.)* |
| **B5** | **Dragging the per-layer OPACITY knob forces a full compositor rebuild every value change.** `SetLayerOpacity` → `MutateLayer` → `MarkCompositionDirty` bumps the version; the render loop's `RefreshRenderer` re-decodes every image from disk and recompiles all shaders. The `_uOpacity` uniform is pushed per-frame but reads a value frozen at build time. | `src/Liveolator.Visuals/Gl/GlVisualPerformanceEngine.cs:238` | The single most common live VJ gesture causes visible stutter and disk thrash — the per-frame uniform path (the correct one) is already wired but reads a stale value. `ContinuousControlViewModel` has no debounce, so every encoder tick rebuilds. | Don't bump the composition version for opacity/blend/visibility; keep live per-layer state the renderer reads each frame so the existing `_gl.Uniform1(_uOpacity, …)` reads a live value. Reserve `MarkCompositionDirty` for layer-set/source changes, and even then rebuild only the affected layer. |
| **B6** | **Re-scanning a Modified file silently destroys the user's manual beat-grid / manual BPM-key edits.** `MediaLibrary.ScanAsync` unconditionally calls `CreateEntryAsync` for every Added/Modified delta, producing a fresh `MusicTrack` with `AnalysisIsManual=false`; the manual-lock guard only protects the background re-analysis pass, never the scan path. | `src/Liveolator.Core/Library/MediaLibrary.cs:145` | A DJ who hand-corrects a BPM or sets a manual grid loses it the next time the file is touched/re-tagged and re-scanned — no warning, no recovery. Violates global standard #7 and the manual-edit-lock intent. | On a Modified delta whose existing entry has `AnalysisIsManual==true`, preserve `Bpm/Key/AnalysisIsManual` (re-stamp only the fingerprint) or flag for explicit re-confirm. Add a manual-edit-survival test. |
| **B7** | **Online key lookup is never applied.** `GetSongBpmClient.ParseFirst` builds `OnlineTrackMetadata(Camelot: null, KeyName: keyName, …)`, but `ApplyOnlineDetails` only ever reads `online.Camelot` (and `Camelot.TryToMusicalKey(null,…)` returns false). The found key is silently discarded. | `src/Liveolator.Core/Library/Music/MusicLibrary.cs:239` | The advertised online key cross-check/fallback (doc 16) does nothing for key — only BPM/genre merge. A keyless track that has a `key_of` from GetSongBPM is never enriched. The MCP tool surfaces the key to agents but the in-app apply path drops it. | Either convert GetSongBPM's `key_of` → Camelot code inside `GetSongBpmClient`, or have `ApplyOnlineDetails` fall back to parsing `online.KeyName` when `Camelot` is null. Add an enrichment test asserting an online-only key updates a keyless track. |
| **B8** | **Global Tab handler breaks keyboard focus traversal across the whole app.** The tunnel-phase handler calls `SelectNextTab`/`SelectPreviousTab` and sets `e.Handled=true` for *any* bare Tab, with no carve-out for focused input controls — pre-empting Avalonia's default focus traversal everywhere. | `src/Liveolator.App/Shell/MainWindow.axaml.cs:35` | A keyboard/screen-reader user cannot Tab between fields in Settings or the Library filter bar — Tab always jumps the whole page. Directly violates the docs/19 keyboard-navigation/accessibility intent. | Only treat Tab as tab-switching with an explicit modifier (Ctrl+Tab / Ctrl+PageDown) or when focus is not inside a focusable input; at minimum, don't handle bare Tab when a `TextBox`/`ComboBox`/editable control is focused. |

### 2.2 Medium

- **Sync re-snap seeks to a latency-shifted position** (`TwoDeckBassEngine.cs:610`) — verified
  *confirmed* but **demoted High→Medium** by the verifier. `slavePhase.PositionSeconds` carries a
  `-lat` compensation that is correct for *error measurement* (it cancels deck-to-deck) but wrong as
  an *absolute seek base*: each re-snap lands exactly `OutputLatencySeconds` behind the beat. Doc 24
  listed this as Low "harmless at 0 latency" — it is now **live**, because production sets
  `OutputLatencySeconds = buffer ms` (`ServiceConfig.cs:541`). Fix: seek from the raw playhead
  (`_backend.GetDeckPositionSeconds(deck.Handle) + ReSnapSeconds`), mirroring `PhaseAlignToLeader`.
- **Tempo autocorrelation not normalized by overlap length** (`TempoEstimator.cs:60`) — larger lags
  accumulate fewer products, biasing selection toward shorter lags / faster BPM. Normalize each lag by
  `(n - lag)` before selecting `bestLag`.
- **BpmNudge on a sync-locked deck is silently dropped** (`TwoDeckBassEngine.cs:438`) — the +/−0.1
  nudge updates the UI feedback but has no audible effect while SYNC is on; a dead control during a
  set. Disable/relabel while synced, or implement a manual phase-bias the lock loop respects.
- **SetLayerSource / ToggleLayer trigger a full-stack rebuild** (`GlVisualPerformanceEngine.cs:229`) —
  a single-layer change re-decodes and re-uploads every other layer and recompiles all shaders (same
  root cause as B5). Rebuild only the changed layer, or cache decoded textures keyed by source ref.
- **Filler layers reference a non-existent generator id `core/vu-meter`** (`GlVisualPerformanceEngine.cs:438`)
  — latent dead reference (invisible at opacity 0 today). Use `VisualSourceKind.None` for filler slots.
- **UI-driven (global) MIDI learn drops relative tick scaling** (`GlobalMidiLearnCoordinator.cs:54`) —
  a jog/encoder learned via click-to-learn binds `RelativeTicksPerRevolution=1.0` instead of ~128, so
  one tick scrubs a whole revolution. Thread the relative metadata through `BeginLearn`.
- **MIDI learn cannot capture/set a relative encoding — always TwosComplement**
  (`MidiLearnSession.cs:67`) — encoders using OffsetBinary/SignedBit invert or garble. Add a
  `relativeEncoding` parameter (or infer it) and expose it in the Mappings UI.
- **`JsonFileSnapshotIo` uses a fixed temp path with no save gate** (`JsonFileSnapshotIo.cs:25`) —
  concurrent saves race/corrupt; the exception escapes `LiveProfileStore`. Mirror `JsonCatalogStore`:
  `SemaphoreSlim` + GUID temp file. Apply to `JsonLiveSetStore` too.
- **VisualControl backend commands are wired in the VM but unreachable from the view**
  (`VisualControlView.axaml:8`) — beat-synced transitions, four layer toggles, VU-meter toggle, add-on
  enable/disable and the effects list are all implemented but not bound; the live VJ surface is reduced
  to per-layer source + opacity. Bind them, or remove the dead VM surface if deferred.
- **Waveform bar-downbeat lines render in the yellow waveform color, not the blue accent**
  (`WaveformStrip.cs:207`) — defeats the "line the amber kick up on the blue downbeat" beat-align read.
  Add a dedicated accent brush bound to `{DynamicResource Accent}`.
- **DI `ServiceProvider` is never disposed on shutdown** (`App.axaml.cs:24`) — native BASS, the MIDI
  session, `MasterClockPump`, and dispatcher feedback subscriptions never get deterministic teardown.
  Subscribe `desktop.ShutdownRequested` to dispose the provider.
- **Tap-tempo / beat actions stop driving visuals when realtime audio is up** (`ServiceConfig.cs:312`)
  — `BeatTapTempo`/`Lock`/`Nudge`/`ResetGrid` mutate `sharedLiveClock`, which nothing observes once
  BASS is present; the headline "tap a tempo, visuals pulse" only works headless. Route beat actions
  through the same `SwitchingBeatClock` the visuals read, or disable/relabel the tap controls when an
  audio-driven master is authoritative.
- **CopyBassNative never verifies required native libs at build** (`Liveolator.App.csproj:68`) — a
  release build can ship with missing libs and report success; Live Mode is then silently dead in the
  artifact. Add a packaging-gated target that errors on missing per-RID libs from the manifest.
- **README advertises a stale "Early design phase"** (`README.md:31,53-57`) — contradicts a working
  1,500-test app; misrepresents resolved decisions (BASS) as open. Rewrite to the integrated reality.
- **No packaging/distribution path exists** (`.github/workflows/ci.yml:39-43`) — distribution is the
  project's hard requirement, yet there is no reproducible per-RID publish with native BASS staged and
  Mac notarization. A release blocker independent of any feature.

### 2.3 Low

- **Hann window uses the symmetric formula but is documented/used as periodic for STFT**
  (`Window.cs:14`) — marginal leakage/COLA inaccuracy + misleading doc. Use `0.5*(1-cos(2πi/size))`.
- **`DeckBpmNudge` comment claims `SetDeckBpm` clamps to the pitch range** (`DeckActionHandler.cs:121`)
  — it saturates at the ±8% rail with no end-of-range indication; reword and consider an at-limit flag.
- **Generator post-effect chain renders at a hardcoded 1280×720, not the live viewport**
  (`LayeredQuadRenderer.cs:305`) — softening/aspect mismatch on 1080p/4K/Retina for any generator that
  declares an effect chain (none of the shipped ones do yet). Size to the live viewport on resize.
- **EQ band controls have no MIDI-learn target in the Mappings UI** (`MappingsViewModel.cs:119`) — a
  user whose EQ knobs don't match the default CC layout cannot re-learn them. Add three `MixerEqBand`
  targets per deck to `BuildTargets()`.
- **docs/14 falsely states there is no CI** (`docs/14:9`) — CI now exists; rewrite to the current stack
  and point at `.github/workflows/ci.yml`.
- **fetch-bass.ps1 synopsis omits BASSFLAC** (`scripts/fetch-bass.ps1:11`) — behavior is identical via
  the shared manifest, but the header comments disagree. Update the synopsis.
- **Graceful-skip tests pass silently instead of using a real skip**
  (`tests/Liveolator.Integration.Tests/FfmpegAudioDecoderTests.cs:56`) — CI shows green for a path that
  never ran. Use `SkippableFact`/`Assert.Skip` so the coverage gap is visible.

---

## 3. Recommendations (cross-cutting, beyond the bug fixes)

1. **Get the suite green and keep it green (B0).** Fix the ReactiveUI global-scheduler isolation (a
   save/restore fixture or a non-parallel collection for the `LibrariesViewModel*` classes); then wire
   the existing CI workflow to **gate merges** (branch protection) so a red suite can't land again —
   the safety net is only worth as much as its enforcement.
2. **Decouple live-mutable layer state (opacity/blend/visibility) from the composition version**
   (`GlVisualPerformanceEngine`). This is the root cause of the two highest-impact visual bugs (B5 +
   the MED full-stack rebuild) and is a small per-layer mutable-state change.
3. **Add a deterministic off-GPU render-loop test for the version→rebuild logic** — a fake renderer
   factory counting rebuilds would have caught the opacity thrash and pins "opacity does not rebuild."
4. **Add an RT-safety contract test for coefficient/gain publication** (B1) — a documented invariant
   plus a stress test hammering `SetCoefficients` from one thread while `Process` runs.
5. **Add half/double-tempo phase-sync tests** (B2) — every current `PhaseAlignmentCalculator` test
   uses identical BPMs, so the cross-tempo failure is invisible.
6. **Add soft-takeover (pickup) for absolute controls** and **deliver the release edge for momentary
   CC controls** — both are contained steps toward the Track-D target and prevent value jumps /
   impossible hold-style actions.
7. **Decompose `ServiceConfig.Build` into per-concern `Wire*` methods** (~470 lines, `realtimeUp` gate
   repeated 6+ times) and **move `LiveClockSelector` from Visuals to Core/Beat** (pure, GL-free logic
   belongs in Core per the iron rule).
8. **Make EQ-kill, pitch range, and keylock first-class configurable behaviors** and **persist the
   memory cue with the track** — the feel gaps a working DJ notices first.
9. **Reconcile online-enrichment + manual-edit provenance through the full loop**: once B6/B7 land,
   add round-trip tests asserting `AnalysisIsManual`/`BpmProvenance`/online key survive save/load, and
   add a single canonical key-normalization step (any notation → Camelot).
10. **Bring product-facing docs to reality** (README, docs/14, docs/15) and **define an explicit
    "v1 / first credible release" scope line** across the four parallel tracks.

---

## 4. Missing features (by track)

**DJ (Track A):** keylock / master-tempo (B4); loop halve/double, loop move, loop roll; press-and-hold
cue-play preview (CDJ audition); slip mode; channel (line) fader separate from gain/crossfader;
end-of-track warning / auto-cue; per-deck VU/clip + true-peak/loudness metering; explicit pitch-range
selector (±6/8/10/16/wide); DC-blocking / denormal protection on the DSP chains.

**Library (Track B):** convert GetSongBPM key names → Camelot (B7); canonical-key normalize step from
any notation; scan-time conflict surfacing for manual entries; resolve the SQLite-vs-JSON store
question (`SqliteCatalogStore` is referenced in docs but **does not exist in code**); library import
from rekordbox / Serato / iTunes.

**VJ (Track C):** scene/layer/effect **authoring UI** (the single biggest VJ gap — scenes are
hand-edited JSON today); video-clip playback into a layer (FFmpeg→GL texture); live camera/capture
input; a real `Transition` crossfade (currently a no-op); strobe overlay; per-layer macro targeting
beyond layer-0 brightness; multiple output windows / external-display targeting.

**Controller (Track D):** Push 1 control-surface profile (the VJ surface); Push 1 SysEx feedback
adapter (pad color palette, LCD text, User-mode switch); mode-aware, context-following control surface;
soft-takeover / pickup engine; relative-encoding capture in MIDI learn; profile import/export UI;
verify and pin the CMD STUDIO 2A CC/note map against the device chart (currently best-effort defaults).

**Sync / clock:** bar/phrase (downbeat) phase alignment, not just beat phase; external master clock /
Ableton Link bridge; internal-clock fallback when the synced master stops or unloads mid-mix; a
per-tick rate-slew limiter (graduated correction).

**Architecture / platform:** deterministic engine shutdown sequence; unified single beat clock
(collapse `sharedLiveClock` vs `sharedVisualClock`); autopilot / show-rules action kinds + handler;
live audio-capture source as a `PerformanceAction`.

**Release:** audible per-deck PFL pre-listen + multi-channel cue output; **cross-platform installer /
packaging + Mac notarization + BASS license management** (the hard requirement, unstarted); a
release/packaging CI job (per-RID publish with native BASS staged); golden-image UiShots; merge gating;
a code-coverage gate.

---

## 5. The 10 next steps (recommended order)

Sequenced for **maximum credibility per unit of effort**: restore the safety net first, then the small
"make built features actually work / stop losing data" fixes, then differentiator correctness, then the
large build-outs.

1. **Get the suite green (B0).** Fix the ReactiveUI global-scheduler isolation behind the 10
   `LibrariesViewModel*` failures (save/restore the `RxApp` statics or a non-parallel collection), then
   wire branch protection to the existing CI so a red suite can't merge. *S — do this first; it
   protects everything after it.*
2. **Decouple live opacity/blend/visibility from the composition version (B5 + MED rebuild).** Turns the
   most common VJ gesture from a stutter-and-disk-thrash into a free per-frame change; add the
   rebuild-counting render-loop test. *S.*
3. **Preserve manual beat-grid/BPM edits across a Modified re-scan (B6).** Stops silent data loss — do
   it early, before anyone relies on manual grids. *S.*
4. **Fix the global Tab handler so focus traversal works (B8).** One-line carve-out; restores app-wide
   keyboard/accessibility. *S.*
5. **Make the live EQ coefficient swap atomic (B1).** Isolated audio-thread-safety fix; removes audible
   clicks under knob automation. *S.*
6. **Make beat loops grid-snap under Quantize (B3) + apply the online key (B7).** Two small fixes: loops
   that land on the kick, and online key enrichment that actually enriches. *S.*
7. **Fix phase alignment across half/double tempos (B2) + the re-snap latency seek (MED).** The
   differentiator must be correct for the most common DJ pairing before more is built on it; add
   half/double-tempo sync tests. *M.*
8. **Dispose the DI provider on shutdown + unify the dual clock so tap-tempo drives visuals (MED×2).**
   Deterministic native teardown, and the headline "tap a tempo, visuals pulse" works with audio up. *S–M.*
9. **Add a packaging/native-staging release pipeline (per-RID `dotnet publish` + BASS manifest + Mac
   notarization) and resolve the BASS license obligation in-repo.** Distribution is the project's
   reason to exist and is entirely unstarted. *M.*
10. **Build keylock / master-tempo via `BASS_FX` (B4) and/or the first slice of the VJ authoring UI +
    effect-execution pipeline.** The two largest remaining gaps — the headline DJ feature and the other
    half of the differentiator. *L.*

> Steps 1 is the gate. Steps 2–6 and 8 are isolated enough to run **in parallel on separate
> worktrees** per the parallel-agent workflow (visuals, library, UI-shell, audio, mixer, app-shell are
> different files) — but finish each through the (now-green) test gate before merge. Steps 7, 9, 10 are
> larger and should be scoped on their own branches.
