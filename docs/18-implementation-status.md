# 18 — Implementation Status (living map)

> **Purpose:** a single, authoritative map of what is **already built** in code, so work is
> not duplicated and so the design docs (numbered 00–17) can stay aspirational while this doc
> tracks reality. Update this file whenever a module lands. Last updated: **2026-06-06**.

## How to read this

- **Layer rule:** platform-agnostic seams + pure logic live in `Liveolator.Core` and are
  unit-tested under `tests/Liveolator.Core.Tests` with **no hardware and no native deps**.
  Native/realtime implementations live in binding projects (`Liveolator.Audio`,
  `Liveolator.Visuals`, the future `Liveolator.Midi`). This mirrors the existing
  `IFileEnumerator` / `IAudioDecoder` pattern.
- **Done** = implemented **and** covered by tests. **Deferred** = intentionally not built yet,
  with the blocker named.

## Core test count

`tests/Liveolator.Core.Tests` — **389 passing** (as of 2026-06-06). Solution-wide: **734**
across 7 test projects (Core 389, Visuals 43, Audio 89, MIDI 27, App 113, Media 49, Integration 25).

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

- **Deferred:** the **native MIDI implementation** of `IMidiInput`/`IMidiOutput`/
  `IMidiDeviceProvider` (a `Liveolator.Midi` project). Blocker: needs the MIDI library +
  hardware; not unit-testable. **Decision:** RtMidi/libremidi (per root `CLAUDE.md`) — note
  doc 05's body still names DryWetMidi and is pending revision; the seams above don't care.

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
  the engine via a new `SetDeckFirstBeat` seam (anchor unknown ⇒ Quantize arms but does not guess). `Cue`
  jumps to the track start (settable cue points later) and pauses. **Loops:** `DeckSetLoop` arrives at the
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
  scales with the pitch fader; Quantize snaps two playing decks into phase without an audible skip.
- **App-wired (`ServiceConfig`):** the single-deck path is replaced by `TwoDeckBassEngine(mixer)` registered
  as `IMultiDeckPlaybackEngine`, with `MasterMixPlaybackEngine`'s clock registered as `IBeatClock` and
  `DeckActionHandler(IMultiDeckPlaybackEngine)` driving both decks. **Headless fallback preserved:** if
  native bass/bassmix is absent the realtime services are simply not registered and the app runs as a
  catalog browser (the Libraries tab's Load→A/B enable off dispatcher feedback, so deck B lights up now).
- **Deferred (next increment):** native `BassMixerBackend` **runtime** verification needs the `bassmix`
  native fetched alongside core bass (update `scripts/fetch-bass`); the per-deck **cue** bus → output
  ch 3/4; **threading the first-beat anchor to the engine end-to-end** — the `SetDeckFirstBeat` seam exists
  and is exercised, but the `DeckLoadTrack` action carries only one numeric (`Value` = BPM), so the
  composition root must call `SetDeckFirstBeat` from the loaded track's `BpmResult.FirstBeatSeconds`
  (App/ServiceConfig wiring, out of this increment's lane); **continuous** phase tracking (this snaps once
  on Quantize-on; doc 11's ±5% proportional correction while playing is a later pass); settable/named
  cue points + hot-cue clear; per-pad hot-cue
  LED feedback (the `ActionFeedbackChanged` model has no cue-index field yet); tempo-preserving pitch (would add
  `ManagedBass.Fx`); and ASIO/CoreAudio multi-channel cue output. The new deck transport is reachable via the
  dispatcher and **Sync is now surfaced in the Live-tab DeckView UI** (the SYNC button drives
  `DeckSyncLockToggle` with active-state feedback); cue/pitch/loop/hot-cue controls there are still disabled
  (a separate UI increment). `SetCue` (mixer
  PFL) still latches the flag only; `BassMixer` still drops controls for an unregistered slot.

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
- **Deferred:** a precomputed/cached overview in the catalog (re-decoding per load is fine for now but
  redundant with analysis); a beat-grid overlay on the strip; click-to-seek on the waveform (would emit
  `DeckSeek`); and surfacing the same strip on the **DJ-tab** deck view (the VM already carries the data).

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

### ✅ GL compositor — first vertical slice — `Liveolator.Visuals/Gl/` (doc 08)

The first concrete `IVisualPerformanceEngine` over OpenGL: **one image-backed fullscreen layer
with one beat-reactive brightness/strobe effect.** Silk.NET (OpenGL + GLFW windowing) + SkiaSharp
for image decode.

| Built | File |
|-------|------|
| Pure per-frame uniform resolution (macro + `BeatClockState` → brightness/flash/blackout) | `Gl/FrameUniforms` |
| Still-image → RGBA8 pixels (degrades via `ImageLoadException`) | `Gl/RgbaImage`, `Gl/SkiaImageLoader`, `Gl/ImageLoadException` |
| Fullscreen-quad GLSL program + GL renderer | `Gl/QuadShaderSource`, `Gl/QuadRenderer`, `Gl/ShaderCompilationException` |
| Concrete engine (SetMacro/Blackout/ActiveBank/CurrentFrame pure; `Run()` opens window + renders) | `Gl/GlVisualPerformanceEngine` |

- **Tested off the GPU (24 tests, green):** `FrameUniforms.Resolve` (macro mapping, confidence-gated
  beat flash, blackout override), `RgbaImage.Validated`, `SkiaImageLoader` (decode / missing /
  non-image), and the engine's pure state via `CurrentFrame()`. GL context creation needs a display,
  so `Run()` is **manually** verified — steps in `Liveolator.Visuals/CLAUDE.md`.
- **Deferred (grow into `GlVisualPerformanceEngine`, not replace it):** the full layer/effect chain
  + blend modes, video + camera sources, quantized scene/clip launching via `IBeatScheduler`,
  transitions/strobe — all currently logged no-ops.

### ✅ Visual action handler — dispatcher → visual engine bridge — `Liveolator.Core/Visuals/` (doc 04/08)

`VisualActionHandler` mirrors `BeatActionHandler`: it owns the `Visual*` action kinds, drives one
`IVisualPerformanceEngine`, and reports feedback (active scene pad, blackout/strobe latch, bank
select) so a Push/UI surface can follow it. Pure Core — unit-tested against a `FakeVisualPerformanceEngine`,
no GL.

| Handled kind | Engine call |
|--------------|-------------|
| `VisualLoadScene` | `LoadScene(ActiveBank.Scene(slot), Immediate)` (out-of-range slot logs + no-ops) |
| `VisualSelectBank` | `SelectBank(slot)` |
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
  never called at composition — launching the render window is a deferred user action (the
  `RENDER-WINDOW SEAM` note). The engine runs off its own `ManualBeatClock`; binding it to the live
  audio clock is part of that seam.

### ✅ Live tab — full performance surface — `Liveolator.App/Features/Live/` (doc 12, the mock)

The Live tab now renders the whole `design/mockups/live-mode-clean.html` layout as composed module
view-models under `Features/Live/Modules/`, each driving the engines only through the dispatcher (doc 04).

| Module | View-model | Wired action(s) |
|--------|------------|-----------------|
| Program Out | `ProgramOutViewModel` | Show Visuals (`IVisualStage`); preview/REC/layers static |
| Beat Engine | `BeatEngineViewModel` | Tap / Lock-toggle / ½× / 2× / Set / Nudge± / **Reset**; Auto disabled |
| Deck A / B | `DeckViewModel` (slot 0/1) | `DeckPlayPause`, `DeckSyncLockToggle` (tempo match), `MixerEqBand` (Hi/Mid/Low), `MixerFilter`; cue/loop/hot-cue/pitch disabled |
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
- **Deferred:** the audio binding over `PlaylistAudioPlayer` (GoToTrack/preload) and the
  `NextTrackPreloader` — blocked on the audio library. The `NowChanged` seam is ready for it.
  Note `Played` history is modeled (enum) but not yet surfaced.

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

## Cross-cutting decisions made while building the above

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
   `JsonCatalogStore`). DI registration deferred to the host (not yet wired in `ServiceConfig`).
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
