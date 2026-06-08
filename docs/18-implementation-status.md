# 18 — Implementation Status (living map)

> **Purpose:** a single, authoritative map of what is **already built** in code, so work is
> not duplicated and so the design docs (numbered 00–17) can stay aspirational while this doc
> tracks reality. Update this file whenever a module lands. Last updated: **2026-06-08**.
>
> **See also `docs/24-system-review-2026-06-07.md`** — a ten-expert full-system review with a
> verified bug map and the recommended next 10 steps. Where doc 24 and this file disagree on a
> code fact, doc 24 wins (it was measured against the working tree on 2026-06-07).

## How to read this

- **Layer rule:** platform-agnostic seams + pure logic live in `Liveolator.Core` and are
  unit-tested under `tests/Liveolator.Core.Tests` with **no hardware and no native deps**.
  Native/realtime implementations live in binding projects (`Liveolator.Audio`,
  `Liveolator.Visuals`, the future `Liveolator.Midi`). This mirrors the existing
  `IFileEnumerator` / `IAudioDecoder` pattern.
- **Done** = implemented **and** covered by tests. **Deferred** = intentionally not built yet,
  with the blocker named.

## Core test count

Solution-wide CI baseline: **1,290 passing, 0 failed, 0 skipped** across 8 test projects, measured
**2026-06-08** (`dotnet test Liveolator.sln --configuration Release --no-restore`): Core 660,
App 235, Audio 166, Media 83, Visuals 71, Integration 25, MIDI 27, Online 23.

The **visual add-on standard + VU meter** wave (doc 26) added: Core +76 (audio-level envelope/meter,
generator-source + effect-role model — Core now ~734), Visuals +4 (audio-level frame uniforms +
generator renderability — now 75), App +2 (level-source + built-in-generator wiring). *Local note:*
on some Windows boxes ~6–10 `LibrariesViewModel*` tests fail from a **pre-existing** ReactiveUI
global-scheduler isolation issue (confirmed failing at the parent commit, unrelated to this wave; CI green).

The growth over
the previously-recorded 851 reflects the
in-flight wave — continuous phase-lock sync (`PhaseLockController`, `PhaseAlignmentCalculator`),
the deck-driven shared clock (`DeckDrivenBeatClock`/`SwitchingBeatClock`/`MasterClockBridge`),
and live-set persistence (`ILiveSetStore`/`JsonLiveSetStore`) — all of which landed with
committed tests.

### ✅ Extension packages, UI themes, visual registry, and audio FX racks — first increment

The managed extension spine in `docs/21-extension-system.md` is built:

| Built | Area |
|-------|------|
| ECDSA P-256 package signatures and SHA-256 payload verification | Core + Media |
| Atomic install registry, enable/disable/uninstall, dependency/path validation | Media |
| Settings package controls and persisted Developer Mode/theme | App |
| Token-only UI themes with Spartan fallback | Core + App |
| Visual-effect descriptors, stable effect instance ids, structured macro targets | Core |
| Isolated shader-probe process contract | Core + Visuals |
| Deck A / Deck B / Master realtime effect racks and dispatcher actions | Core + Audio |
| Isolated VST3 scanner client, quarantine/cache behavior, native bridge contract | Audio |

- Rack processing is after deck gain/EQ/filter and on the post-mix master before output/beat
  analysis. Rack snapshots are copy-on-write; `Process` itself takes no locks and allocates nothing.
- Missing VST3 processors are pass-through placeholders that retain identity and saved state.
- Rack order, bypass, parameters, plugin UID, opaque state, and missing-plugin placeholders persist
  under `live/audio-fx-racks.json` and restore on startup.
- **Native delivery still required:** the repository does not vendor the Steinberg SDK or ship the
  scanner, native bridge implementation, or shader-probe executables. Real VST3 processing and
  extension-shader activation remain unavailable until those distribution artifacts are supplied.
- **Visual compositor limitation remains:** the GL engine now composites a **multi-layer** stack
  with per-layer blend + opacity and beat flash off the shared clock (see "GL compositor" below),
  but **arbitrary effect chains are not yet implemented** — `EffectRef` chains are dropped before
  the renderer (`SceneComposition`), so no GLSL effect executes. Package/registry contracts are
  ready; the per-layer effect pass is the remaining piece. **Note (doc 24):** the running render
  loop also does not yet re-read the scene after the window opens, so live scene/bank/layer/opacity
  changes do not reach the GL output — only brightness/flash/blackout uniforms are live.

## Module status

### ✅ Performance Action system — `Liveolator.Core/Actions/` (doc 04)

The action-layer seam: every input source drives engines through one dispatcher.

| Built | File |
|-------|------|
| Action model (serializable record + enums) | `PerformanceAction`, `PerformanceActionKind`, `ActionInputMode` |
| Feedback model | `ActionFeedbackState`, `ActionFeedbackChanged` |
| Dispatcher seam + impl | `IPerformanceActionDispatcher`, `PerformanceActionDispatcher` |
| Handler seam + base | `IPerformanceActionHandler`, `PerformanceActionHandlerBase` |
| UI-thread marshaling seam | `IActionFeedbackSynchronizer`, `InlineActionFeedbackSynchronizer` |

- Routing is **data, via handler registration** (no giant switch); duplicate-kind ownership
  fails fast at construction. Handler failures are logged with action context and swallowed;
  unknown kinds log a warning, never throw.
- **Deferred:** the concrete concern handlers land with their engines. Built so far:
  **`BeatActionHandler`** (see Beat), **`PlaylistActionHandler`** (see Live playlist),
  **`VisualActionHandler`** (see Visual action handler), **`DeckActionHandler`** (see Realtime audio —
  load/play-pause/stop **+ seek/pitch/cue/sync-lock/quantize/hot-cue/loop**, slot-addressed), and
  **`MixerActionHandler`** (see Software mixer — Crossfade/ChannelGain/EqBand/Filter/CueToggle).
  Deck tempo can be controlled directly in audible BPM via `DeckBpm`; it shares the same rate state
  as `DeckPitch`, clamps to the engine's ±8% pitch range, and reports the effective BPM back to UI/MIDI.
  **All Deck kinds are now claimed:** `DeckSetLoop` arrives via the handler (`Value` = beat length;
  `> 0` sets a beat-length loop at the playhead, `<= 0` clears it) and the engine converts beats → a
  time region using the per-deck base BPM threaded in by `SetDeckBaseBpm`; hot-cues are done (cue index
  rides in `Argument`).

### ✅ Controller mapping engine — `Liveolator.Core/Mapping/` (doc 05)

Pure MIDI→`PerformanceAction` translation + device seams + routing. **Library-agnostic.**

| Built | File |
|-------|------|
| Library-agnostic message + binding model | `MidiMessage`, `MidiMessageType`, `ControllerBinding`, `ControllerMappingProfile`, `ValueCurve`, `RelativeEncoding` |
| Value conversion (absolute+curve, 3 relative encodings, 14-bit pitch bend, velocity) | `ControlValueConverter` |
| Matching (NoteOn-vel0=NoteOff, pitch-bend per-channel) | `BindingMatcher` |
| Mapper → dispatcher | `IControllerMapper`, `ControllerMapper` |
| Conflict detection (no silent winner) | `MappingConflict`, `MappingConflictDetector` |
| MIDI learn (deterministic, user-overridable) | `IMidiLearnSession`, `MidiLearnSession` |
| Device seams | `IMidiInput`, `IMidiOutput`, `IMidiDeviceProvider` |
| Input routing (learn-aware) | `MidiControllerRouter` |
| Auto-select profile by device name | `MidiProfileSelector` |
| Action feedback → LED output | `MidiFeedbackPublisher` |
| Live composition (router+mapper+feedback over one opened device) | `MidiInputPipeline` |
| CMD STUDIO 2A default profile (learn-overridable) | `Mapping/Profiles/CmdStudio2AProfile` |

- **Native MIDI implementation built** — `Liveolator.Midi` (`RtMidiDeviceProvider` etc., RtMidi.Core,
  per root `CLAUDE.md`); native is isolated behind `IRtMidiDeviceManager` so translation/lookup
  unit-test with fakes (doc 05 body still names DryWetMidi — historical; the seams don't care).
- **Hardware now drives the dispatcher (this increment):** `MidiInputPipeline` (Core, pure) composes
  `MidiControllerRouter → ControllerMapper →` the one `IPerformanceActionDispatcher` over an opened
  `IMidiInput`, auto-selecting a profile via `MidiProfileSelector` and (when a feedback output is
  present) wiring `MidiFeedbackPublisher` back out. The Core seam `IMidiDeviceProvider` gained
  `OpenInput`/`OpenOutput` (already implemented by `RtMidiDeviceProvider`) so the App opens a device
  through the seam without touching RtMidi types. `CmdStudio2AProfile.Default` maps the controller's
  transport (`DeckPlayPause`/`DeckCue`), sync (`DeckSyncOnce`), crossfader + per-deck gain
  (`MixerCrossfade`/`MixerChannelGain`), 3-band EQ (`MixerEqBand` Low/Mid/High) + filter
  (`MixerFilter`), and track-position jog (`DeckJog`) to the existing kinds — Deck A = channel 0/slot
  0, Deck B = channel 1/slot 1. **The CC/note numbers are documented defaults, not gospel:** every
  binding is a plain `ControllerBinding` that `MidiLearnSession` can re-capture, and
  `MappingConflictDetector` proves the default layout is collision-free. +23 tests (Core
  `CmdStudio2AProfileTests` 11 + `MidiInputPipelineTests` 7; App `MidiInputWiringTests` 5 — graceful
  degradation with a fake provider). **Real-hardware behaviour is a documented MANUAL checklist**
  (`Liveolator.Midi/CLAUDE.md`), not automatable in CI.
- **App-wired (`MidiControlSession`):** the composition root opens the SETTINGS-chosen controller
  (`AppSettings.Midi`) via the shared `RtMidiDeviceProvider` and registers the live session so DI
  disposes it (closing the device) at shutdown. When no persisted mapping exists, a known controller
  receives its matching shipped default profile instead of an empty profile; Settings Save also
  reconnects the selected input/output immediately without requiring a restart. **Degrades gracefully**
  (global standards #16/#26): no controller selected, no matching device, or a native open failure all
  log + leave the app running WITHOUT MIDI — never throw at startup. The Settings tab reuses the same
  provider instance.
- **Mappings UI is live:** the `MAPPINGS` tab exposes the active profile, target selection, MIDI
  learn, cancel, and binding removal. Learn captures the next real hardware message, replaces any
  binding that targets the same action or physical control, applies it immediately, and persists the
  profile under the connected device name so it reloads on the next launch.
- **Global MIDI Learn:** the shell's `MIDI LEARN` mode intercepts the next
  `PerformanceAction` emitted by any active UI control, uses that full action target (kind, slot, and
  argument) to arm the hardware capture, and suppresses the UI action itself. The next controller
  message is applied and persisted, then the mode returns to waiting for another UI control. `Esc`
  cancels the pending capture and exits Learn mode.
- Learn preserves the UI control's input semantics instead of guessing from MIDI message type:
  buttons remain momentary/toggle even when hardware sends CC, while sliders and positional knobs
  remain absolute. Momentary/toggle CC bindings ignore the zero-valued release message, preventing
  one physical click from firing twice; absolute controls still accept zero as a valid endpoint.
- Continuous MIDI feedback now reaches the visible controls: mixer channel gain, EQ bands, filter,
  cue level/mix, crossfader, deck pitch/seek, and visual macro values publish through
  `FeedbackChanged`; the matching UI control applies the value without re-dispatching it.
- **DJ jog-wheel transport:** jog bindings are relative and carry encoder encoding, inversion, and
  ticks-per-revolution in `ControllerBinding`. `DeckJog` converts wheel revolutions to real track
  seconds (independent of track length): one full turn scrubs 1.8 s while paused and a fine 0.2 s
  while playing. The engine clamps at track boundaries and immediately publishes `DeckSeek`
  feedback so the on-screen playhead/waveform follows the hardware. Existing saved CMD profiles
  using the former beat-clock jog mapping are upgraded in place.
- **Deferred:** persisted/custom mapping profiles beyond the CMD STUDIO 2A default feeding
  `AvailableMidiProfiles` (the `ILiveProfileStore` round-trip exists); the Push 1 profile + SysEx
  LED/LCD formatting (doc 06); and confirming the CMD STUDIO 2A CC map against its MIDI implementation
  chart (until then the defaults are learn-overridable best-effort).

### ✅ Beat engine primitives — `Liveolator.Core/Beat/` (doc 03)

The shared audio/visual clock foundation that needs no audio frames.

| Built | File |
|-------|------|
| Output state + seam | `BeatClockState`, `TempoCandidate`, `BeatClockSource`, `IBeatClock` |
| Link-style timeline (host-time↔beat bijection) | `IBeatTimeline`, `BeatTimeline` |
| Quantization | `Quantize`, `IBeatScheduler` (seam), `BeatQuantizer` (resolver) |
| Confidence-gated launch (shared audio/visual) | `QuantizedLaunch` |
| Tap tempo (pure) | `TapTempoService` |
| Manual clock (`BeatClockSource.Manual`) | `ManualBeatClock`, `IBeatClockControl` |
| Host-time seam | `IHostClock`, `SystemHostClock` |
| **First real dispatcher handler** | `BeatActionHandler` (Tap/Lock/Unlock/Half/Double/Nudge±/Reset/SetDownbeat + lock LED feedback) |

- **End-to-end loop proven in tests:** `PerformanceActionDispatcher` → `BeatActionHandler` →
  `ManualBeatClock`, with feedback back through the dispatcher.
- **Realtime beat clock now built:** `AudioBeatClock` (see Realtime audio) drives a live
  `BeatClockState` from the frame pipeline using `SpectralFlux` + the existing `TempoEstimator`.
- **Deferred:** `BeatClockSource.External` (Ableton Link), and a dedicated per-deck `BeatTracker`
  for two-deck sync (doc 11). Note: **offline** BPM/key analysis also exists under `Core/Analysis/`.

### ✅ Realtime audio chain — `Liveolator.Core/Audio/` + `Liveolator.Audio/Playback/` (docs 01/02)

The live capture→analysis→clock path, plus playback driven through the action layer. The pure
seams + composition live in Core; the native BASS backend lives in the Audio binding.

| Built | File |
|-------|------|
| Source seam + sample batch | `IAudioSource`, `AudioSamplesAvailable` (Core) |
| Frame pipeline seam + impl | `IAudioFrameProvider`, `AudioFrameData`, `SpectrumAnalyzer`, `AudioFramePipeline` (Core) |
| Fixed-analysis-rate resampling | `LinearResampler` (Core/Dsp); opt-in `AudioFramePipeline(analysisSampleRate:)` |
| Track-swappable source | `SwitchableAudioSource` (Core) |
| Playback engine seam + composition | `IAudioPlaybackEngine`, `IDeckSourceFactory`, `LivePlaybackEngine` (Core) |
| Master-mix → clock composition (two-deck) | `MasterMixPlaybackEngine` (Core) |
| Two-deck engine + master source seam | `TwoDeckBassEngine`, `MasterAudioSource`, `IBassMixerBackend` (Audio) |
| Deck transport handler | `DeckActionHandler` (Core; load/play-pause/stop + seek/pitch/cue/sync-lock/quantize/hot-cue) |
| **BASS realtime backend** | `BassAudioEngine`, `DeckAudioSource`, `BassPlayback`, `IBassPlayback` (Audio) |
| Capture seams | `IAudioCaptureDeviceCatalog`, `IAudioCaptureSourceFactory`, `AudioCaptureDevice`, `CaptureSourceKind` (Core) |
| **BASS capture backend** | `BassCaptureEngine`, `CaptureAudioSource`, `BassCaptureBackend`, `ICaptureBackend` (Audio) |

- **Decision made:** realtime audio library = **BASS/ManagedBass** (2026-06-05). All BASS calls go
  through the internal `IBassPlayback` seam so `DeckAudioSource` unit-tests with a fake; native
  bass is not needed in CI. `BassAudioEngine` implements `IDeckSourceFactory`.
- `LivePlaybackEngine` wires `SwitchableAudioSource → AudioFramePipeline → AudioBeatClock` and
  exposes the live `IBeatClock`; the deck swaps per track without breaking the clock. Proven
  end-to-end in Core tests (synthetic click track → 120 BPM detected).
- **App slice:** the Libraries tab plays the selected track via the dispatcher and shows the live
  detected BPM. Live Mode is best-effort: if native BASS is absent, the app runs as a catalog
  browser with transport hidden.
- **Fixed analysis rate:** `AudioFramePipeline` optionally resamples the downmixed mono to a fixed
  analysis rate (`LinearResampler`, Core/Dsp) before framing, so tempo analysis is consistent across
  44.1/48/96 kHz sources. Opt-in via the `analysisSampleRate` constructor parameter; omitted = native
  rate (original behaviour). Frames are stamped with the analysis rate and stay timestamp-continuous,
  so `AudioBeatClock` envelope-rate derivation is unchanged.
- **Half-time ambiguity correction:** `TempoEstimator` checks the double-time harmonic when a result
  below 100 BPM has a supported intermediate-beat peak. Fast tracks with alternating strong/weak
  beats no longer collapse to half tempo, while genuinely slow tracks without intermediate onsets
  remain at their detected BPM. The same estimator serves offline analysis and the realtime clock.
- **Native BASS setup wired:** `scripts/fetch-bass.(ps1|sh)` fetch the per-platform un4seen BASS lib
  into `runtimes/<rid>/native/` (git-ignored; commercial license — see `docs/01`), and the App build
  (`CopyBassNative` target) copies it next to the output, warning when absent. `ServiceConfig.Build()`
  + `BassAudioEngine` construction are covered by guarded tests that pass without native BASS in CI
  (App `ServiceConfigTests`, Audio `BassAudioEngineSmokeTests`). Real sound-output + LIVE-BPM
  verification is a documented **manual** hardware checklist (`docs/01`), not automatable here.
- **Deferred:** ASIO/CoreAudio device selection + multi-channel cue output (doc 01 Phase 1b / doc 11),
  and the system-loopback capture source.
- **Capture sources (task 8, first increment):** `CaptureAudioSource` emits `AudioSamplesAvailable`
  exactly like `DeckAudioSource`, so a system-loopback or line-input feed plugs straight into the
  same `SwitchableAudioSource → AudioFramePipeline → AudioBeatClock` path. BASS calls are isolated
  behind the internal `ICaptureBackend` seam (mirrors `IBassPlayback`) so the source state machine
  unit-tests with a fake — native bass is not needed in CI. `BassCaptureEngine` implements both
  `IAudioCaptureDeviceCatalog` (enumerate) and `IAudioCaptureSourceFactory` (create), and is
  registered in `ServiceConfig`. **Windows:** loopback + line-in via BASS record devices (the WASAPI
  loopback endpoint appears as a record device named "…loopback"). **macOS:** line-in works through
  the same record path; system-loopback needs a virtual device (e.g. BlackHole) the user installs —
  documented on `BassCaptureBackend`; swapping in the BASSWASAPI add-on later does not change Core or
  the source state machine.
- **Deferred:** ASIO/CoreAudio device selection + multi-channel cue output (doc 01 Phase 1b / doc 11),
  resampling to a fixed analysis rate, the **Settings/Live-tab device-picker UI** (seam left in
  `ServiceConfig.WireCaptureSources` with a note), and wiring source selection as a `PerformanceAction`.

### ✅ Software mixer (first increment) — `Liveolator.Core/Mixer/` + `Liveolator.Audio/Playback/` (doc 11)

The two-deck software mixer's pure model + DSP math + action handler, plus a thin BASS-side routing
seam. Crossfader/EQ/filter math is pure and unit-tested; native FX routing into live deck channels is
the next increment.

| Built | File |
|-------|------|
| Immutable mixer model (2 deck slots) | `MixerState`, `DeckChannelState`, `EqBands`, `CrossfaderCurve`, `EqBand` (Core) |
| Pure DSP math (crossfader gains, combined deck gain, RBJ biquad EQ/filter design) | `MixerMath`, `BiquadCoefficients` (Core) |
| Realtime mixer seam | `IMixer` (Core) |
| Dispatcher handler | `MixerActionHandler` (Core; Crossfade/ChannelGain/EqBand/Filter/CueToggle) |
| BASS routing skeleton + per-deck native seam | `BassMixer` (`IMixer` impl), `IBassMixerChannel` (Audio) |

- `MixerActionHandler` holds the authoritative `MixerState`, derives audible gains + biquad
  coefficients via `MixerMath`, and pushes them to `IMixer` — driven only through the dispatcher.
  Crossfader curves: Smooth (constant-power, default), Linear, Sharp. EQ = low/high shelf + mid peak;
  single-knob filter sweeps LP below center / HP above. Coefficient designs are unit-tested for
  bypass-at-flat, boost/cut direction, LP/HP direction, and impulse-response stability.
- **Deck slots:** `DeckActionHandler` now addresses decks by `PerformanceAction.Slot` (A=0/B=1). The
  existing single-deck engine is adapted to slot 0 (`SingleDeckEngineAdapter`), so the single-deck
  path and its tests are unchanged; a two-deck engine implements the new `IMultiDeckPlaybackEngine`.
- **App:** `ServiceConfig` wires `BassMixer` + `MixerActionHandler` into the dispatcher and registers
  `IMixer`, so UI/controllers can drive the mixer now.
- **Loaded decks persist across restarts:** `IDeckSessionStore` / `JsonDeckSessionStore` save Deck A
  and Deck B under `live/deck-session.json` (track path, analyzed BPM, and first-beat anchor).
  `DeckSessionPersistence` restores them through the shared dispatcher without auto-playing, ignores
  missing files, and keeps later loads saved. `DeckActionHandler` retains load feedback so deck UI
  instances created after startup still show the restored title, tempo, and waveform.
- **Per-deck level meters are live:** each `BassMixerChannel` measures post-gain/EQ/filter/effect peak
  and RMS without allocating on the audio path. `IDeckLevelMeter` exposes snapshots to the shared
  mixer view-model, and each channel fader renders a segmented green/amber/red meter that updates on
  the existing UI timer and clears when its deck stops.
- **Headphone cue (PFL) is now audible (A2):** `CueMixMath` (pure, Core) carries the cue-bus math —
  per-deck pre-fade send, equal-power cue/master blend, level-scaled headphone gains, and the per-deck
  cue contribution (`DeckCueContributionGain`). `BassMixerBackend` now feeds **both** legs of the cue
  mixer: the master leg (post-limiter master) and the cued-deck leg (each cue-enabled deck's pre-fade
  samples, scaled by the cue-leg gain, pushed into the cue device). So enabling Cue routes a deck into the
  headphones independently of the crossfader/master. The per-deck cue-send routing is native (manual-verify
  on the CMD STUDIO 2A); the gain math is unit-tested.
### ✅ Two-deck DJ engine + master mix → clock — `Liveolator.Audio/Playback/` (doc 11, increment 2)

The two decks now feed one master mix, the mixer's gain/EQ/filter route to real per-deck channels, and
the beat clock follows the audible mix — the increment that turns the routing skeleton into a real engine.

| Built | File |
|-------|------|
| Two-deck engine (slot-addressed; registers channels into `BassMixer`) | `TwoDeckBassEngine` (`IMultiDeckPlaybackEngine`, Audio) |
| Post-crossfader master mix exposed as a source | `MasterAudioSource` (Audio) |
| Master-mix → frame pipeline → beat clock composition | `MasterMixPlaybackEngine` (Core) |
| Native BASS calls behind a seam (mirrors `IBassPlayback`) | `IBassMixerBackend` + `BassMixerBackend` (Audio) |
| Per-deck realtime gain + cascaded EQ/filter (managed DSP) | `BassMixerChannel` + `StatefulBiquad` (Audio) |

- **Closes the missing seam:** `TwoDeckBassEngine.Load(slot,…)` plugs a decoding deck stream into the
  BASSmix master and **registers an `IBassMixerChannel` into `BassMixer`**, so `MixerActionHandler`'s
  gain/EQ/filter (computed by `MixerMath`) finally reach a real per-deck channel — before this they were
  dropped (no channel registered).
- **Beat clock follows the mix:** `MasterMixPlaybackEngine` feeds the single `MasterSource` →
  `AudioFramePipeline` → `AudioBeatClock`, so analysis sees the post-crossfader signal (doc 11), not a
  switched single deck. Proven end-to-end in tests (synthetic click through the master → 120 BPM).
- **EQ/filter are applied in managed DSP, not BASS_FX:** `BassMixerChannel` runs the Core
  `BiquadCoefficients` via `StatefulBiquad` (per-audio-channel Direct-Form-I state) inside the deck's
  BASS DSP callback — keeping Core's mixer math authoritative and the sample processing unit-testable.
  So only **`ManagedBass.Mix`** is needed (no `ManagedBass.Fx`).
- **Transport added (seek/pitch/cue/sync-lock/quantize/hot-cue):** `IMultiDeckPlaybackEngine` gained per-slot
  `Position`/`Seek`, `PitchPosition`/`SetPitch`, `Cue`, `SyncLock`/`Quantize` toggles, and hot-cues
  (`HotCueCount`, `IsHotCueSet`, `HotCue`) — all routed by `DeckActionHandler` (with value/active feedback; the
  hot-cue index rides in the action `Argument`). `TwoDeckBassEngine` implements them over three new
  `IBassMixerBackend` calls — `GetDeckPositionFraction`/`SetDeckPositionFraction` (via `BassMix.Channel*Position`)
  and `SetDeckRate` (vinyl-style **pitch = playback-rate**; tempo+pitch move together, so still no `ManagedBass.Fx`).
  The pitch fader and the sync/quantize toggles are **per-slot state that persists across track loads** (the
  rate is re-applied to the newly loaded deck); **hot-cues (8/deck) belong to the loaded track and clear on
  reload** — first press sets at the current position, next press jumps to it. **Sync-lock now does tempo
  match** (beatmatch by BPM, doc 11): `TempoSyncCalculator` (Core, pure — ½×/2× fold) sets the follower's
  rate to `leader_bpm / deck_bpm`; leader = the other deck (automatic); the analyzed BPM reaches the engine
  via a new `SetDeckBaseBpm` seam fed from `DeckLoadTrack.Value`. **Quantize now does a real phase match**
  (doc 11): enabling it snaps the deck's playhead so its beat phase lines up with the sync leader's grid.
  `PhaseAlignmentCalculator` (Core, pure — shortest signed nudge within ±½ beat) computes the seconds to
  move; the engine seeks the deck by it. The per-track **first-beat (downbeat) anchor** it needs is now in
  `BpmResult.FirstBeatSeconds` (computed by `FirstBeatEstimator`, a new third BPM-pipeline stage) and reaches
  the engine via a new `SetDeckFirstBeat` seam (anchor unknown ⇒ Quantize arms but does not guess).
  **`Cue` is a settable temporary cue (A5, CDJ back-to-cue):** the pure `CueButtonResolver` (Core) decides
  set-vs-return from transport state + live position + the stored temp cue (with an at-cue tolerance) —
  pressing while paused at a fresh spot drops the cue there; pressing while playing (or paused at the cue)
  returns to it (track start when none is set) and pauses; the temp cue clears on reload. (Press-and-hold
  cue-play preview is deferred — the action seam carries no button release.) **Persistent hot cues (A3):**
  the engine now takes an optional `IHotCueStore`; on Load it restores the track's saved cue set (keyed by
  path) into the hot-cue bank, and a newly set cue is persisted back. `HotCuePositionMapper` (Core, pure)
  converts the deck's 0..1 fraction to/from the store's sample offset using the deck length + master rate;
  the store is wired in `ServiceConfig` (`JsonHotCueStore`). Tolerant: a missing/unreadable store, a load
  that throws, or a save that throws all degrade to RAM-only cues — never crash the show. **End-of-track
  (A4):** the engine arms a backend end-of-stream callback (`SetDeckEndCallback`) on Load and raises a
  slot-tagged `DeckEnded` event when a deck runs out; `PlaylistAudioPlayer` listens and calls the live
  queue's `NotifyTrackEnded` so it auto-advances (or stops when dry). **Loops:** `DeckSetLoop` arrives at the
  engine, which turns a beat length into a `[start, end)` time region via `BeatLoopCalculator` (Core, pure)
  using the per-deck base BPM, and arms it over two new `IBassMixerBackend` calls (`SetDeckLoop` =
  `BassMix.ChannelSetSync(BASS_SYNC_POS|Mixtime)` seeking back to the in-point, `ClearDeckLoop`).
- **Testability:** all BASS interop sits behind `IBassMixerBackend`; the load/play/stop state machine, the new
  transport (seek/pitch/cue/sync/quantize/hot-cue), channel registration, master-tap→clock spine, and the
  biquad/gain processing — plus the new **loops** and **phase-match** — all unit-test with fakes; native
  bass/bassmix is not in CI (the native `BassMixerBackend` is verified manually, like `BassPlayback`).
  The pure math has its own Core tests: `PhaseAlignmentCalculatorTests`, `BeatLoopCalculatorTests`,
  `FirstBeatEstimatorTests`. **Manual-verify checklist (native, not in CI):** loop in/out is click-free and
  sample-accurate (BASS_SYNC_POS Mixtime wrap); a 4-beat loop is musically 4 beats at the deck tempo; loop
  scales with the pitch fader; Quantize snaps two playing decks into phase without an audible skip;
  **enabling Cue on a deck is audible in the headphones (PFL) independent of the crossfader/master (A2);**
  **a deck running to its end fires the end-sync so the live queue auto-advances (A4);** **a persisted hot
  cue recalls at the same musical position after an app restart (A3).**
- **App-wired (`ServiceConfig`):** the single-deck path is replaced by `TwoDeckBassEngine(mixer)` registered
  as `IMultiDeckPlaybackEngine`, with `MasterMixPlaybackEngine`'s clock registered as `IBeatClock` and
  `DeckActionHandler(IMultiDeckPlaybackEngine)` driving both decks. **Headless fallback preserved:** if
  native bass/bassmix is absent the realtime services are simply not registered and the app runs as a
  catalog browser (the Libraries tab's Load→A/B enable off dispatcher feedback, so deck B lights up now).
- **A2–A5 landed (`feat/dj-decks-perfect`):** **PFL headphone cue is audible (A2)** — each cue-enabled
  deck's pre-fade samples are summed into the cue mixer scaled by the level-scaled cue-leg gain (per-deck
  push into the cue device); the gain math is pure (`CueMixMath.DeckCueContributionGain`) and the per-deck
  cue-send routing is native (manual-verify on the CMD STUDIO 2A ch 3/4). **Persistent hot cues (A3),
  settable temporary cue (A5), and end-of-track auto-advance (A4)** are wired and unit-tested (see the deck
  engine + live-playlist notes). The first-beat anchor is threaded end-to-end (A1, prior wave).
- **Deferred (next increment):** native `BassMixerBackend` **runtime** verification needs the `bassmix`
  native fetched alongside core bass (update `scripts/fetch-bass`); **continuous** phase tracking (Quantize
  snaps once on enable; doc 11's ±5% proportional correction while playing is a later pass); **press-and-hold
  cue-play preview** (needs a button release the action seam does not carry); per-pad hot-cue LED feedback
  (the `ActionFeedbackChanged` model has no cue-index field yet); tempo-preserving pitch (would add
  `ManagedBass.Fx`); and ASIO/CoreAudio multi-channel cue output. The deck transport is reachable via the
  dispatcher and **all the transport controls are surfaced in the DeckView UI** (shared by the Live and
  DJ tabs): Sync/Play, **Cue** (`DeckCue`), **Loop** (`DeckSetLoop`), the **hot-cue pads** (`DeckHotCue`
  per index, each pad lit from feedback via the cue index in `ActionFeedbackState.Argument`), and the
  **Pitch** fader (`DeckPitch`). `BassMixer` still drops controls for an unregistered slot.

### ✅ Deck waveform — overview + playhead, end-to-end — `Liveolator.Core/Waveform/` + `Liveolator.Audio/Waveform/` + `Liveolator.App` (doc 11)

The track-overview waveform the decks draw, built **data-first** then wired through the action layer to the
UI: a pure peak model + reducer in Core, an offline decode→peaks provider in the Audio binding, a
data-driven strip control, and a deck VM that learns its track from `DeckLoadTrack` feedback.

| Built | File |
|-------|------|
| Immutable overview (0..1 peaks, width-independent) | `WaveformOverview` (Core) |
| Pure reducer (mono PCM → N max-abs buckets; transients preserved, clamped, upsamples) | `WaveformBuilder` (Core) |
| Provider seam | `IWaveformProvider` (Core) |
| Decode→reduce provider over the offline `IAudioDecoder` | `DecodedWaveformProvider` (Audio) |
| Data-driven strip (real peaks + played/ahead split + playhead; decorative fallback) | `WaveformStrip` (App/Controls) |
| Deck VM waveform + playhead | `DeckViewModel.Waveform`/`Progress` (App) |

- `WaveformBuilder` takes **max-abs per bucket** (not an average) so transients survive the downsample;
  amplitudes clamp to 0..1 and every bucket is filled even when buckets > samples. Pure — unit-tested with
  synthetic arrays.
- `DecodedWaveformProvider` decodes to mono at a low **overview sample rate** (`DefaultOverviewSampleRate`
  8 kHz — fidelity is wasted on a strip), accumulates, then reduces via `WaveformBuilder`. Failures
  **degrade, never throw**: an undecodable/failing/empty track returns `WaveformOverview.Empty` so the deck
  falls back to its placeholder; cancellation propagates. Tested with a fake decoder (no FFmpeg/BASS).
  +12 tests (Core `WaveformBuilderTests` 6, Audio `DecodedWaveformProviderTests` 6).
- **UI wired (the render + playhead):** `ServiceConfig` registers `IWaveformProvider` →
  `DecodedWaveformProvider` and threads it through `LiveViewModel`/`DjViewModel` into both decks. The deck
  learns its loaded track from **`DeckLoadTrack` feedback** — `ActionFeedbackState` gained an optional
  `Argument` (mirrors `PerformanceAction.Argument`) so `DeckActionHandler` reports the path on load, the only
  load-time signal back to subscribers. `DeckViewModel` then sets the title, kicks an **off-thread** overview
  decode (cancelling any prior in-flight load on a fast deck swap), and exposes `Waveform` + `Progress`.
  `WaveformStrip` renders real mirrored peaks with a played/ahead colour split + a playhead line, falling
  back to its decorative bars when no track is loaded. **Headless-safe:** the overview uses the offline
  decoder (works with no realtime BASS); the playhead polls `DeckSeek` position feedback (`Unavailable`
  without a realtime engine → stays at 0).
- **Completed in this increment:** a **beat-grid overlay** on the strip (the deck derives 0..1 beat-line
  fractions via the pure `BeatGridCalculator` from the load BPM — now echoed in `DeckLoadTrack` feedback
  `Value` — and the decoded duration, now carried on `WaveformOverview.DurationSeconds`); **click-to-seek**
  (the strip maps the clicked X to a 0..1 fraction and emits `DeckSeek` through `DeckViewModel.SeekCommand`);
  and the strip is **already surfaced on the DJ tab**, which shares `DeckView`/`DeckViewModel` via the
  `ViewLocator`.
- **Deferred:** a precomputed/cached overview in the catalog (re-decoding per load is fine for now but
  redundant with analysis); a first-beat-anchored grid (the grid currently assumes the first beat is at the
  track start — it needs the per-track downbeat anchor that analysis does not yet record); and a
  continuously advancing playhead during playback (still needs a render-loop tick).

### ✅ Settings — audio output / buffer / MIDI device selection — `Liveolator.App/Features/Settings/` (doc 12)

The Settings tab detects the available equipment and persists the user's audio + MIDI choices. Models and
seams are pure Core; enumeration is a thin native binding; the UI logic is unit-tested with fakes.

| Built | File |
|-------|------|
| Settings model (output device id + buffer; MIDI in/out names) with clamp/normalize | `AudioSettings`, `MidiSettings`, `AppSettings` (Core/Settings) |
| Output-device picker seam + model | `IAudioOutputDeviceCatalog`, `AudioOutputDevice` (Core/Audio) |
| Settings persistence seam + JSON store (versioned, atomic, tolerant) | `ISettingsStore` (Core) → `JsonSettingsStore` (Media, `settings.json`) |
| BASS output-device enumeration | `BassOutputDeviceCatalog` (Audio) |
| Detect/select/persist view-model + view | `SettingsViewModel`, `SettingsView` (App) |

- **Detects** output devices (`IAudioOutputDeviceCatalog`), capture inputs (`IAudioCaptureDeviceCatalog`),
  and MIDI input/feedback devices (`IMidiDeviceProvider`); **selects** the sound-card output, the output
  **buffer** (latency vs. glitch-resistance, clamped to 10–200 ms), and the MIDI controller/feedback; and
  **persists** the choice via `ISettingsStore`. A previously-selected device that is gone falls back to the
  system default / "(none)" rather than erroring. Buffer is clamped + normalized so a hand-edited config
  can't push an out-of-range value into the device.
- **App-wired (`ServiceConfig`):** the three catalogs + `JsonSettingsStore` + `SettingsViewModel` are
  registered, the SETTINGS tab now shows it, and the App project gained a reference to `Liveolator.Midi`
  (its first use) — `RtMidiDeviceProvider` is registered as `IMidiDeviceProvider`. Headless-safe: enumeration
  degrades to empty when native bass/rtmidi is absent. +21 tests (Core `AppSettingsTests` 8, Media
  `JsonSettingsStoreTests` 6, App `SettingsViewModelTests` 7).
- **Output device + buffer now applied at startup:** `ServiceConfig.Build()` loads `AppSettings` once
  (tolerant; blocking is fine in the composition root) and threads `Audio` into `TwoDeckBassEngine` →
  `BassMixerBackend`, which opens the chosen BASS device and sets the playback buffer before init. The
  `AudioSettings`→BASS mapping is the pure, unit-tested `BassInitOptions` (device-index string → BASS index,
  buffer clamp; null/blank/"0"/stale → default device); a saved device that is gone **falls back to the
  system default** rather than disabling all audio. Native init still verified manually. +5 Audio tests
  (`BassInitOptionsTests`).
- **Runtime device re-init (now built):** saving a device/buffer change re-opens the output **without an
  app restart** via the pure `AudioReinitCoordinator` (Core) → `IAudioEngineReinitializer` seam →
  `BassAudioEngineReinitializer`/`TwoDeckBassEngine.ReinitializeOutput` → `BassMixerBackend.ReinitOutput`
  (native: re-inits the device, re-routes the live mixer via `Bass.ChannelSetDevice`, verified manually).
  The coordinator only re-opens when the device or buffer actually changed (reuses `BassInitOptions`), and
  **rolls back to the last working device** + logs if the re-open fails — never leaves the app silently
  without audio. The Settings tab surfaces the outcome in `Status`. +6 Core `AudioReinitCoordinatorTests`,
  +2 Audio `TwoDeckBassEngineTests`.
- **Persisted capture-source selection (now built):** `AudioSettings` gained a tolerant
  `CaptureDeviceId`+`CaptureSource` pair (folded to "no capture" when half-written/blank; trimmed);
  `JsonSettingsStore` round-trips it as **optional** snapshot fields so an older version-1 config without
  them still loads. The Settings tab shows a capture-source picker (led by a "(none)" sentinel) and applies
  the choice through the existing `WireCaptureSources` factory via the Core `ICaptureSourceController`
  (`CaptureSourceController` creates+starts the source and routes it into a stable `SwitchableAudioSource`,
  disposing the prior source; a failed open keeps the current source). Wired as a `PerformanceAction` was
  **not** chosen — there is no live capture consumer in the pipeline yet, so source selection goes through
  the factory seam directly (the action route can be added once a live-capture engine consumes the switch).
  +9 Core (`AppSettingsTests` capture cases + `CaptureSourceControllerTests`), +2 Media
  `JsonSettingsStoreTests`, +9 App `SettingsViewModelTests`.
- **Deferred (next increment):** opening the chosen **MIDI controller** into `MidiControllerRouter` so the
  hardware drives the dispatcher (needs the `ControllerMapper`+profile pipeline composed in `ServiceConfig`,
  not yet wired). Applying the buffer/device to `BassPlayback` (the legacy single-deck path) is still open.
  Feeding the capture `SwitchableAudioSource` into the analysis/beat pipeline (so a selected loopback/line-in
  actually drives the live BPM/visuals) and modelling source selection as a `PerformanceAction` are the
  remaining capture increments.
- **MIDI controller now opened into the dispatcher** (see Controller mapping engine — `WireMidiInput`
  composes `MidiInputPipeline` over the SETTINGS-chosen device; degrades gracefully when it is
  absent). **Still deferred:** **runtime** re-init on a device change (the current apply is at startup
  only — re-plugging or changing the controller in Settings needs an app restart), and applying the
  buffer/device to `BassPlayback` (the legacy single-deck path). Persisted capture-source selection
  (the `WireCaptureSources` SETTINGS-UI seam) remains open.
- **Live MIDI control pipeline now wired (`ServiceConfig`):** the chosen controller is opened at startup and
  routed through the one dispatcher, so hardware drives the same handlers as the UI. `MidiControlSession`
  (Core/Mapping) orchestrates it — `IMidiDeviceProvider.OpenInput/OpenOutput` (promoted onto the seam) →
  `ControllerMapper` (profile loaded via `ILiveProfileStore`, empty fallback) → `MidiControllerRouter` +
  `MidiFeedbackPublisher` (LEDs) + `MidiActivityMonitor` (connection pulse). Best-effort/headless-safe: no
  controller, absent rtmidi, or no saved profile leaves it idle / routing an empty profile without blocking
  startup. +17 tests (Core `MidiActivityMonitorTests` 3, `MidiControlSessionTests` 7; App
  `ShellStatusViewModelTests` 4 + provider-seam fakes).
- **Shell top-bar status (`Liveolator.App/Shell/`):** `ShellStatusViewModel` + the `MainWindow` status strip
  show **where audio is routed** (selected output device name) and **which MIDI gear is connected**; the MIDI
  LED flashes a dedicated **green** (`MidiActive` token — a deliberate exception to the single-blue line) on
  each inbound message, driven off `IMidiControlStatus.ActivityDetected` via an Rx `Throttle`.
- **Deferred (next increment):** a **MIDI-learn UI** to author/save device profiles (the routing pipeline is
  ready and routes whatever profile is loaded). **Runtime** re-init on a device change (MIDI + audio apply at
  startup only) and applying the buffer/device to `BassPlayback` (the legacy single-deck path) are also open.
  Persisted capture-source selection (the `WireCaptureSources` SETTINGS-UI seam) remains open.

### ✅ Visual scene model (performance layer) — `Liveolator.Core/Visuals/` (doc 08)

The high-level scene/bank/macro vocabulary and quantized-launch logic that sits **above** the
GPU compositor — pure data + math, no GL.

| Built | File |
|-------|------|
| Vocabulary records | `VisualScene`, `VisualLayer`, `VisualBank`, `VisualSourceRef`, `EffectRef`, `BeatBehavior` |
| Enums | `BlendMode`, `TransitionStyle`, `VisualSourceKind` |
| Macro + mapping (normalized→target range) | `VisualMacro`, `MacroTarget` |
| Engine seam | `IVisualPerformanceEngine` |

- Reuses `Beat.Quantize` and `Beat.QuantizedLaunch` (the **same** clock/quantum as audio — the
  product differentiator), so the visual quantize is not a separate mechanism.
- `VisualBank.Scene(i)` returns null out of range (an empty pad). `VisualMacro.Resolve` clamps
  to 0..1 then maps to `[Min,Max]`. `VisualLayer` validates opacity.

### ✅ GL compositor — multi-layer + blend + live-clock — `Liveolator.Visuals/Gl/` (doc 08)

The concrete `IVisualPerformanceEngine` over OpenGL: the active scene's **full layer stack**
composited with per-layer **blend modes + opacity**, beat-reactive off the **shared live clock**.
Silk.NET (OpenGL + GLFW windowing) + SkiaSharp for image decode.

| Built | File |
|-------|------|
| Pure per-frame uniform resolution (macro + `BeatClockState` → brightness/flash/blackout) | `Gl/FrameUniforms` |
| Still-image → RGBA8 pixels (degrades via `ImageLoadException`) | `Gl/RgbaImage`, `Gl/SkiaImageLoader`, `Gl/ImageLoadException` |
| Pure scene→layer resolution (order/blend/opacity/renderability) | `Gl/SceneComposition`, `Gl/ResolvedLayer` |
| Pure `BlendMode` → premultiplied-alpha GL blend factors (Overlay degrades to Normal) | `Gl/BlendModeGl` |
| Pure live-clock selection (audio-driven master clock else manual tap clock) | `Gl/LiveClockSelector` |
| Fullscreen-quad GLSL program + multi-layer GL renderer | `Gl/LayeredQuadShaderSource`, `Gl/LayeredQuadRenderer`, `Gl/ShaderCompilationException` |
| Concrete engine (SetMacro/Blackout/ActiveBank/**multi-bank SelectBank**/CurrentFrame/CurrentComposition pure; `Run()` opens window + renders the layer stack) | `Gl/GlVisualPerformanceEngine` |

- **Tested off the GPU (63 tests, green):** `FrameUniforms.Resolve` (macro mapping, confidence-gated
  beat flash, blackout override), `RgbaImage.Validated`, `SkiaImageLoader` (decode / missing /
  non-image), `SceneComposition` (order/blend/opacity carry-over, image-only renderability),
  `BlendModeGl` (separable-mode factors, Overlay rejected), `LiveClockSelector` (audio-preferred /
  manual-fallback), and the engine's pure state via `CurrentFrame()` / `CurrentComposition()`. GL
  context creation needs a display, so `Run()` is **manually** verified — steps in
  `Liveolator.Visuals/CLAUDE.md`.
- **Bank selection is now real (doc 22 C3):** the engine holds an ordered, non-empty list of banks
  (`BankCount`/`ActiveBankIndex`/`BankNames`); `SelectBank(index)` switches the active bank (an
  out-of-range index logs a warning and is ignored — not a silent no-op), and the next composed frame
  reads the new `ActiveBank`, so no GL-thread coordination is needed. Tested off the GPU (active bank +
  composition follow the selection; out-of-range ignored; empty/null bank list rejected). The single-bank
  constructor still works (one-element list), preserving the first-slice behaviour.
- **Deferred (grow into `GlVisualPerformanceEngine`, not replace it):** video + camera layer sources
  (resolve as non-renderable and are skipped today), quantized scene/clip launching via
  `IBeatScheduler`, transitions/strobe, and the per-layer effect chain — all currently logged no-ops.
  `Overlay` blend awaits a framebuffer read-back path (degrades to Normal for now). **Per-pad scene
  loading still renders only the active bank's first scene** (`LoadScene` remains a logged no-op on the
  GL side — needs a display to verify), so switching banks changes which scene set the pads address and
  what the next frame composites, but lighting an individual pad's scene into the GL output is the
  remaining GL-render piece.

### ✅ Visual action handler — dispatcher → visual engine bridge — `Liveolator.Core/Visuals/` (doc 04/08)

`VisualActionHandler` mirrors `BeatActionHandler`: it owns the `Visual*` action kinds, drives one
`IVisualPerformanceEngine`, and reports feedback (active scene pad, blackout/strobe latch, bank
select) so a Push/UI surface can follow it. Pure Core — unit-tested against a `FakeVisualPerformanceEngine`,
no GL.

| Handled kind | Engine call |
|--------------|-------------|
| `VisualLoadScene` | `LoadScene(ActiveBank.Scene(slot), Immediate)` (out-of-range slot logs + no-ops) |
| `VisualSelectBank` | `SelectBank(slot)` — now switches the engine's **active bank** (doc 22 C3), not a no-op |
| `VisualSetMacro` | `SetMacro(Argument, Value)` (missing name logs + no-ops) |
| `VisualToggleLayer` / `VisualSetLayerOpacity` | `ToggleLayer(slot)` / `SetLayerOpacity(slot, Value)` |
| `VisualLaunchClip` | `LaunchClip(slot, Argument, Immediate)` (missing clip id logs + no-ops) |
| `VisualBlackout` / `VisualToggleStrobe` | `Blackout`/`Strobe` bool latch held by the handler, fed back |
| `VisualTransitionNow` / `NextBeat` / `NextBar` | `Transition(style, Quantize.Immediate/NextBeat/NextBar)` |

- **Quantized transitions** map the action kind to the shared `Quantize` quantum and let the engine
  resolve the fire time against the one shared beat clock via `Beat.QuantizedLaunch` — the handler
  does not double-resolve timing.
- **Deferred:** `VisualSetLayerSource` is **not** claimed — the `PerformanceAction` record carries
  only `Slot`/`Value`/`Argument` and cannot express a `VisualSourceRef`. Claim it once the action
  payload (or an `Argument`-keyed source registry) exists; the engine seam is already in place.
- **App-wired (`ServiceConfig.WireVisuals`):** the `GlVisualPerformanceEngine` is registered as
  `IVisualPerformanceEngine` and the handler joins the one dispatcher. **Headless-safe:** `Run()` is
  never called at composition — launching the render window is a deferred user action via
  `IVisualStage` (the `RENDER-WINDOW SEAM` note). The engine binds to the `LiveClockSelector`-chosen
  clock: the audio-driven master clock when the realtime BASS engine is up (visuals lock to the
  audible signal), else the shared manual tap clock (headless) — closing the clock half of the seam.
  The realtime engine is now constructed *before* `WireVisuals` so its master clock is available to
  the selector.
- **Banks are real, not hardcoded (doc 22 C3):** `ILiveProfileStore.ListVisualBankNamesAsync` enumerates
  the saved banks under `live/scenes/` (tested in `Liveolator.Media`); `ServiceConfig.LoadBanksOrStarter`
  loads them all (startup bank "Live" first → active on launch, the rest in name order), and feeds the
  ordered list to the multi-bank `GlVisualPerformanceEngine` (falling back to the placeholder starter
  bank when none are saved). The Scene Grid's bank tabs are driven by the engine's real `BankNames`
  (`SceneGridViewModel` → `LiveViewModel` → `ServiceConfig`), so selecting a tab maps `VisualSelectBank`
  to actual bank data — the engine then switches its active bank. **Remaining GL-render piece:** lighting
  an individual pad's scene into the GL output (`LoadScene`) still needs a display to verify (above).

### ✅ Visual add-on standard — generators + live audio level — `docs/26` (Core + Visuals + App)

The public contract for third-party visual add-ons (the **VU meter** is its first reference add-on,
shipped built-in and live). Two gaps the old "effect = texture post-process" model could not serve are
now closed: **generative** shaders that draw from uniforms, and **live audio amplitude** reaching shaders.

| Built | File |
|-------|------|
| Live audio level snapshot + VU ballistics (pure, dt from frame timestamps) | `VisualAudioLevel`, `AudioLevelEnvelope` (Core/Audio) |
| Level read seam + frame-driven meter + headless fallback | `IVisualAudioLevelSource`, `FrameAudioLevelMeter`, `SilentVisualAudioLevelSource` (Core/Audio) |
| Generative source kind + effect role on the descriptor (default Effect) | `VisualSourceKind.Generator`, `VisualEffectRole`, `VisualEffectDescriptor.Role` (Core/Visuals) |
| Audio uniforms in the per-frame model (`uRms`/`uPeak`/`uLevel`) | `FrameUniforms` (Visuals/Gl) |
| Generator pass (viewport FBO, re-rendered each frame, no input texture) | `GeneratorPass` (Visuals/Gl) |
| Generator-layer compositing + audio/`uResolution` uniforms in the effect chain | `LayeredQuadRenderer`, `EffectChainRenderer`, `SceneComposition` (Visuals/Gl) |
| Built-in VU-meter generator (shader + descriptor, the reference add-on) | `VuMeterAddon` (Visuals/Gl) |

- **Pure, tested off the GPU:** the envelope ballistics (attack faster than release, RMS/peak, NaN/empty
  guards), the frame meter (tracks frames, silent before any, disposes its subscription), the descriptor
  role JSON round-trip (default `Effect`), generator renderability in `SceneComposition`, and the audio
  level flowing through `FrameUniforms.Resolve`. +~25 tests (Core `AudioLevelEnvelopeTests`,
  `FrameAudioLevelMeterTests`, `VisualEffectDescriptorTests`; Visuals `SceneCompositionTests`/`FrameUniformsTests`;
  App `ServiceConfigTests`).
- **The engine reads `IVisualAudioLevelSource.Current` from the render thread**, exactly like it already
  reads `IBeatClock.Current` (a level/clock sample is the read path, not the dispatcher command path). The
  level meter taps the **same master-mix frames** the beat clock reads (`MasterMixPlaybackEngine.FrameProvider`);
  headless it is `SilentVisualAudioLevelSource`. Published via a `volatile` whole-record swap of an
  immutable `VisualAudioLevel` (single audio-thread writer, lock-free render-thread read).
- **Out of the box:** `ServiceConfig` registers the built-in VU generator into the effect registry and the
  starter bank carries a `Generator` layer, so a fresh install shows the analog meter; its needle swings
  with the master `uLevel` (verified manually — GL needs a display; see `Liveolator.Visuals/CLAUDE.md` step 8).
- **Deferred:** per-band spectrum uniforms (only RMS/peak/VU level now); a generator that *also* has a
  post-effect chain uses a fixed chain size (the VU has none); Push/MIDI mapping of generator parameters
  end-to-end (the macro plumbing is reused, a dedicated mapping is later).

### ✅ Visual library / VJ tab — asset browser — `Liveolator.App/Features/VisualLibrary/` (doc 08/13, Track C **C1**)

The VJ tab now browses the **existing** visual catalog instead of a placeholder. It mirrors the music
Libraries tab over `VisualMediaLibrary` (images + video clips), reusing the same Core scan/catalog
infrastructure the MCP `scan_visual_folders`/`list_visuals` tools use — no duplicated scanning logic.

| Built | File |
|-------|------|
| Pure, reusable visual-asset query (text + kind + status facets, title order) | `Core/Library/Visual/VisualAssetQuery` (`VisualAssetFilter`) |
| Core catalog-store seam for visual assets + visual scan folders | `Core/Persistence/IVisualCatalogStore` |
| Media binding (one `JsonCatalogStore` now implements both music + visual seams; `scan-folders.visual.json`) | `Media/JsonCatalogStore` |
| Tab view-model: add folders, incremental scan (probe dims/duration), filter, restore, persist | `App/Features/VisualLibrary/VisualLibraryViewModel` |
| Row VM (kind glyph, dimensions, duration, status) + filter-label converter + view | `VisualAssetRowViewModel`, `VisualFilterLabelConverter`, `VisualLibraryView.axaml` |

- **Tested (Core + Media + App, all green):** `VisualAssetQuery` facet composition / text match / limit
  clamp; `JsonCatalogStore` visual-scan-folder round-trip + corrupt-file tolerance + separate-from-music;
  `VisualLibraryViewModel` restore, kind/status/text filtering, scan→probe→persist, per-file failure
  isolation. No GL needed — the browser is pure VM + catalog, like the music tab.
- **Wired (`ServiceConfig`):** `IVisualMediaProbe` = `CompositeVisualMediaProbe` (image header reader +
  ffprobe), `VisualMediaLibrary`, `IVisualCatalogStore`, and `VisualLibraryViewModel` are registered;
  the VJ tab hosts it (`MainWindowViewModel`) and `App.OnFrameworkInitializationCompleted` restores it
  at startup. Thumbnails are intentionally a kind **glyph** (no decode) — image thumbnailing is a later,
  optional step.

### ✅ Live tab — full performance surface — `Liveolator.App/Features/Live/` (doc 12, the mock)

The Live tab now renders the whole `design/mockups/live-mode-clean.html` layout as composed module
view-models under `Features/Live/Modules/`, each driving the engines only through the dispatcher (doc 04).

| Module | View-model | Wired action(s) |
|--------|------------|-----------------|
| Program Out | `ProgramOutViewModel` | Show Visuals (`IVisualStage`); preview/REC/layers static |
| Beat Engine | `BeatEngineViewModel` | Tap / Lock-toggle / ½× / 2× / Set / Nudge± / **Reset**; Auto disabled |
| Deck A / B | `DeckViewModel` (slot 0/1) | `DeckPlayPause`, `DeckSyncOnce` (one-shot tempo + phase match), `MixerEqBand` (Hi/Mid/Low), `MixerFilter`; cue/loop/hot-cue/pitch disabled |
| Mixer | `MixerViewModel` | `MixerCrossfade`, `MixerChannelGain` (A/B); VU static |
| Scene Grid | `SceneGridViewModel` + `ScenePadViewModel` | 8×8 `VisualLoadScene`, bank `VisualSelectBank`; pad state from feedback |
| Master / FX | `MasterFxViewModel` | `VisualToggleStrobe`, `VisualBlackout`; Master/Swing disabled |
| Push Encoders | `MacroEncodersViewModel` + `ContinuousControlViewModel` | 8× `VisualSetMacro` |

- **One dispatcher (architecture change):** `ServiceConfig.Build()` now composes a **single**
  `IPerformanceActionDispatcher` (Beat + Mixer + Visual handlers always; Deck transport when the BASS
  engine is up) registered unconditionally, replacing the previous two-dispatcher split. The Live tab and
  Libraries tab share it, so handler state never diverges (doc 12, one source of truth). Libraries playback
  stays gated on `IAudioPlaybackEngine` (no dead Play button headless). `IMixer`/`MixerActionHandler` are
  now wired headless (no native needed — `BassMixer` drops calls for unregistered slots).
- **Feedback-driven UI:** sliders/pads/toggles seed from `GetFeedback` and follow `FeedbackChanged`;
  `ContinuousControlViewModel.SetFromFeedback` updates the bound control without re-emitting (no loop).
- **Disabled + labeled** controls match the mock for capabilities with no Core handler yet (doc 18):
  Auto, deck cue/loop/hot-cues/pitch, Master-gain/Swing, VU levels, waveforms, footer telemetry.
- Default landing tab switched to **Live**. App tests: 70 (module emission + feedback + the one-dispatcher
  composition). Real render verified manually — the GL window + live audio still need hardware.

### ✅ Live playlist queue — `Liveolator.Core/Playlist/` (doc 09)

Performance-editable Now/Next/Later queue. Pure in-memory editing logic; the audio binding
subscribes to `NowChanged` and drives the underlying player.

| Built | File |
|-------|------|
| Queue model | `QueueEntry`, `TrackState` |
| Seam | `ILivePlaylist` |
| Editable queue + safe skip | `LivePlaylist` |
| Dispatcher handler | `PlaylistActionHandler` (Insert/Move/Remove/SkipOnNextBar) |

- Editing `Upcoming` (insert-next/move/remove) **never raises `NowChanged`** — playback is
  undisturbed (the doc 09 success criterion). `Now` is protected from removal; stale ids from a
  laggy UI are logged at debug and ignored. `SkipOn(...)` defers through `IBeatScheduler`.
- **Audio binding now built** (`Liveolator.Audio/Playback/`): `PlaylistAudioPlayer` subscribes to
  `NowChanged` and drives `IMultiDeckPlaybackEngine` (load + auto-play on the bound deck slot; stops
  the slot when the queue runs dry), and `NextTrackPreloader` warms `Upcoming[0]` via the new pure
  Core seam `IDeckPreloader`. Both are tolerant: a failed track load/preload is logged and dropped so
  a bad track never crashes the show or stalls the queue (global #16/#26). Sequencing is unit-tested
  with fakes (no native BASS) — Audio `PlaylistAudioPlayerTests` + `NextTrackPreloaderTests`.
  **App-wired (`ServiceConfig.WirePlaylistAudio`):** the player binds deck A only when the realtime
  engine is up; headless it stays a catalog browser and the queue still edits freely.
- **End-of-track auto-advance now wired (A4):** `PlaylistAudioPlayer` also subscribes to the engine's
  new slot-tagged `DeckEnded` event and calls `ILivePlaylist.NotifyTrackEnded()` for its bound slot, so a
  deck running out auto-advances the queue (or stops when dry) instead of going silent. The bound-slot
  filter, ignore-other-slot, and unsubscribe-on-dispose paths are unit-tested with fakes; the engine raises
  `DeckEnded` off a native BASS end-of-stream sync (`SetDeckEndCallback`), which is the manual-verify part.
- **Deferred:** the **native `IDeckPreloader`** (pre-buffering the upcoming BASS stream, verified
  manually) — the pure preloader sequencing + seam are ready for it; `NextTrackPreloader` is wired
  only once an `IDeckPreloader` is registered. Note `Played` history is modeled (enum) but not yet surfaced.

### ✅ Autopilot rule engine — `Liveolator.Core/Autopilot/` (doc 10)

Runs an unattended show from rules, emitting actions through the **same** dispatcher a human uses
(doc 04) — so it inherits engine integration for free. Fully pure and testable.

| Built | File |
|-------|------|
| Rule model | `AutopilotRule`, `RuleTrigger`, `TriggerKind`, `RuleCondition`, `Cooldown` |
| Show definition | `AutopilotRuleSet`, `ScenePool`, `AutopilotOverridePolicy`, `OverrideMode` |
| Tick inputs + seam | `AutopilotTickContext`, `IAutopilotEngine` |
| Engine | `AutopilotEngine` |

- Triggers: EveryNBeats / EveryNBars / OnDownbeat / OnTrackPosition. Conditions gate on
  confidence / energy window / track-position window. Per-rule cooldowns prevent flicker.
- **Controlled randomness:** scene-selecting actions draw from a curated `ScenePool` with a
  per-scene cooldown, via a **seeded** `Random` (reproducible/deterministic shows).
- **Override state machine:** AutoResume (suspend N bars then resume) and PauseUntilReenabled,
  both behind one machine; `Stop()` hard-stops. A throwing rule is disabled for the session and
  logged, never stalling the tick loop.
- **Deferred:** nothing in Core — the host drives `Tick(...)` from the clock loop and calls
  `OnManualAction()` for human-sourced actions.

## Pre-existing Core (built before this status doc)

- `Core/Dsp/` (FFT, windows), `Core/Analysis/` (offline BPM, chroma, key/Camelot, cues),
  `Core/Library/` (incremental scan, music + visual catalogs, **tag-metadata seam
  `ITrackMetadataReader` + `TrackMetadata`**), `Core/Playlist/` (`HarmonicSetBuilder`).
  Bindings: `Liveolator.Audio` (WAV + FFmpeg-CLI **offline** decode, **+ tag metadata via
  `AtlMetadataReader` / ATL.NET `z440.atl.core`, MIT**, **+ realtime BASS playback**, see
  Realtime audio),
  `Liveolator.Platform` (file enumerator), `Liveolator.Visuals` (image/video probes only),
  `Liveolator.Media` (JSON catalog store — music snapshot **v2** carries `TrackMetadata`),
  `Liveolator.Mcp` (music-intelligence server).
- **App Libraries tab** (`Liveolator.App/Features/Libraries`) surfaces the scanned catalog:
  track table with an Artist column + a detail panel showing tags (artist/album/genre/year/
  track #), stream facts (bitrate/sample-rate/channels/codec), tempo+confidence, key+name,
  and Camelot harmonic matches. **State persists across runs:** the tab restores the scan
  folders + analyzed catalog at startup (`LibrariesViewModel.InitializeAsync`) and saves both
  after every scan / folder add, via the `IMusicCatalogStore` Core seam wired in `ServiceConfig`
  to `JsonCatalogStore` (`%APPDATA%/Liveolator/{catalog.music.json,scan-folders.json}`).
- **✅ Search / filter / sort UI (doc 22 B1):** a filter/sort bar wires the previously-unused
  `TrackFacets` into Artist/Genre/Year/FileType facet dropdowns, adds a status filter
  (Ok/Partial/Failed) over `TrackFilter.Status`, and adds sort by Title/BPM/Key/Duration with an
  asc/desc toggle + a Clear-filters button. The text search still composes with the facets. All
  filter/sort logic funnels through one `ApplyFilter` in `LibrariesViewModel` over the **pure Core**
  `TrackQuery.Apply` + a new pure `TrackSort` (`TrackSortKey`; missing values sort last either
  direction, Title is the stable tie-break, `Camelot.SortIndex` orders the key column around the
  wheel). The Libraries view also applies a one-minute minimum duration, hiding known-short files
  while retaining tracks whose duration could not be determined. Facets rebuild from the catalog on
  scan/restore and drop stale selections. Tested: Core
  `TrackSortTests` (7), App `LibrariesViewModelFilterSortTests` (8).
- **Per-track re-analysis and manual corrections:** every track context menu can force a fresh local
  BPM/key analysis and optionally cross-check GetSongBPM/AcoustID for BPM/key/genre. The App activates
  lookup from `LIVEOLATOR_GETSONGBPM_KEY` plus optional `LIVEOLATOR_ACOUSTID_KEY` and
  `LIVEOLATOR_FPCALC_PATH`; without keys, local analysis still runs. A track editor persists manual
  BPM, Camelot key/scale, genre, and notes to the music catalog and marks the analysis manual so
  background re-analysis does not overwrite it. The Libraries detail panel shows notes and the
  required GetSongBPM attribution link.
- **✅ Sample-folder designation UI (doc 22 B2):** each row in the Folders window has a "Samples"
  checkbox (`FolderStatusViewModel.IsSampleFolder`) that calls through to `MusicLibrary.SetSampleFolders`,
  reclassifies the cached catalog in place (Track ↔ Sample, no re-decode), refreshes rows + facets, and
  persists via `IMusicCatalogStore.Save/LoadSampleFoldersAsync`. The designation is re-applied to a
  restored catalog at startup and to newly-scanned files. Tested: App
  `LibrariesViewModelSampleFolderTests` (3).
- **✅ Played-history surfacing (doc 22 B5):** `TrackState.Played` is now produced and shown — `DjViewModel`
  records each track as it leaves the Now slot into a most-recent-first `Played` list, rendered as a
  read-only "PLAYED" section under the live set (hidden until something plays). The advance-vs-reload
  distinction is made **in the view-model** (from the expected-next id captured on each `NowChanged`); the
  `LivePlaylist` engine and all audio paths are untouched. Tested: App `DjViewModelPlayedHistoryTests` (4).

## Cross-cutting decisions made while building the above

### Track-linked visual programs - model and persistence foundation (doc 25, Milestone 1 partial)

The authored-data foundation for linking images/video clips to a music track is built:

- `TrackVisualProgram`, `TrackVisualCue`, track/asset references, fit/playback/fallback enums, and
  strict validation live in `Liveolator.Core/Visuals/TrackPrograms/`.
- `TrackVisualCueResolver` resolves the active cue and maps original track time to source time,
  including loop wrapping and once-mode end clamping.
- `ITrackVisualProgramStore` is implemented by `JsonTrackVisualProgramStore`, storing one versioned,
  atomic file per normalized-track-path SHA-256 under `live/track-visuals/`.
- Concurrent saves are serialized; corrupt, mismatched, and incompatible snapshots degrade to a
  warning plus no program. The store is registered in `ServiceConfig`.
- **Deferred:** assignment/editor UI, deck coordinator, image autoplay, video decoding, and
  crossfader-driven deck visual layers (doc 25 Milestones 2+).

- `Liveolator.Core` now references **`Microsoft.Extensions.Logging.Abstractions`** (abstractions
  only — keeps Core pure managed) to satisfy the mandatory-logging standard. Concrete providers
  are wired in host projects.
- All new seams follow the **immutable-record-for-state / interface-for-behavior** style and
  inject time (`IHostClock`) rather than reading a static clock, so everything stays
  deterministic and testable.

## What is safe to build next (no blockers)

1. ~~**JSON persistence** of mapping profiles / scenes / rule-sets under the Live root (doc 13).~~
   **Built** — `ILiveProfileStore` (Core seam, `Liveolator.Core/Persistence`) +
   `LiveProfileStore` (`Liveolator.Media`) round-trip `ControllerMappingProfile`, `VisualBank`
   (with its scenes), the `VisualMacro` set, and `AutopilotRuleSet` as versioned snapshots under
   `live/{mappings,scenes,autopilot}/<name>.json` and `live/macros.json`. Atomic temp-then-move
   saves; tolerant loads (null/empty + warning on corrupt/old-version, mirroring
   `JsonCatalogStore`). **Now DI-wired:** `ServiceConfig` registers `ILiveProfileStore` →
   `LiveProfileStore`, and **surfaces load-at-startup** by loading the authored visual bank
   (`live/scenes/Live.json`) to feed the GL visual engine when present (scenes → banks), falling
   back to the placeholder starter bank when missing/corrupt (tolerant, like `JsonCatalogStore`).
   Mapping-profile load-into-the-MIDI-list lands with the MIDI input pipeline (still deferred).
2. **MCP tools** exposing the new Core capabilities to agents (doc 17).
3. Remaining concern handlers (Visual/Deck/Mixer/Transport) as their engines land.

## Audio-library decision — RESOLVED (BASS/ManagedBass, 2026-06-05)

The realtime audio library is decided and the first vertical slice is built: `IAudioSource`
realtime playback, the audio frame pipeline (doc 02), and audio-driven beat detection (doc 03
realtime half) are all landed (see Realtime audio). What still remains to build on top:

- **Decks + mixer (doc 11):** two-deck playback, software mixer (crossfader/EQ/filter), hot cues,
  loops, **tempo-sync built** (phase-match/quantize pending a beat anchor), multi-channel ASIO/CoreAudio
  cue output. Unblocked — partially built.
- **Capture sources:** system-loopback / sound-card input (doc 01 Phase 1b) — **first increment built**
  (`BassCaptureEngine` + `CaptureAudioSource`, see Realtime audio). Remaining: ASIO device pick and the
  Settings device-picker UI.
- **Decks + mixer (doc 11):** the software mixer's first increment is **built** (pure model + DSP
  math + `MixerActionHandler` + `BassMixer` routing skeleton; decks now slot-addressed — see Software
  mixer). Still to build: the master/cue bus into the frame pipeline, loops, **phase-sync (Quantize)**
  (tempo-sync is built), and multi-channel ASIO/CoreAudio cue output.
- **Capture sources:** system-loopback / sound-card input (doc 01 Phase 1b) and ASIO device pick.
