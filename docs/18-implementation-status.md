# 18 — Implementation Status (living map)

> **Purpose:** a single, authoritative map of what is **already built** in code, so work is
> not duplicated and so the design docs (numbered 00–17) can stay aspirational while this doc
> tracks reality. Update this file whenever a module lands. Last updated: **2026-06-05**.

## How to read this

- **Layer rule:** platform-agnostic seams + pure logic live in `Liveolator.Core` and are
  unit-tested under `tests/Liveolator.Core.Tests` with **no hardware and no native deps**.
  Native/realtime implementations live in binding projects (`Liveolator.Audio`,
  `Liveolator.Visuals`, the future `Liveolator.Midi`). This mirrors the existing
  `IFileEnumerator` / `IAudioDecoder` pattern.
- **Done** = implemented **and** covered by tests. **Deferred** = intentionally not built yet,
  with the blocker named.

## Core test count

`tests/Liveolator.Core.Tests` — **292 passing** (as of 2026-06-05).
`tests/Liveolator.Core.Tests` — **322 passing** (as of 2026-06-05).

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
  **`DeckActionHandler`** (see Realtime audio — DeckLoadTrack/DeckPlayPause/TransportStop, now
  slot-addressed), and **`MixerActionHandler`** (see Software mixer —
  Crossfade/ChannelGain/EqBand/Filter/CueToggle). Pending: Visual + the rest of Deck/Transport
  handlers (hot-cue/loop/seek/pitch/sync).

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
| Deck transport handler | `DeckActionHandler` (Core; DeckLoadTrack/DeckPlayPause/TransportStop) |
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
- **Deferred (next increment):** the two-deck BASS engine + `IBassMixerChannel` BASS_FX
  implementation (applying gain/EQ/filter to real channels), the master/cue bus mix feeding the
  `AudioFramePipeline`, beatmatching/sync-lock/quantize, hot-cues, loops, and ASIO/CoreAudio
  multi-channel cue output. `BassMixer` drops controls for an unregistered slot (logged, never throws)
  until decks register their channels.

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
  transitions/strobe — all currently logged no-ops. **Not yet app-wired:** `ServiceConfig` is
  untouched; the engine reaches the dispatcher only once a `VisualActionHandler` exists (build it
  mirroring `BeatActionHandler`).

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
  and Camelot harmonic matches.

## Cross-cutting decisions made while building the above

- `Liveolator.Core` now references **`Microsoft.Extensions.Logging.Abstractions`** (abstractions
  only — keeps Core pure managed) to satisfy the mandatory-logging standard. Concrete providers
  are wired in host projects.
- All new seams follow the **immutable-record-for-state / interface-for-behavior** style and
  inject time (`IHostClock`) rather than reading a static clock, so everything stays
  deterministic and testable.

## What is safe to build next (no blockers)

1. **JSON persistence** of mapping profiles / scenes / rule-sets under the Live root (doc 13) —
   pure serialization over the records already built.
2. **MCP tools** exposing the new Core capabilities to agents (doc 17).
3. Remaining concern handlers (Visual/Deck/Mixer/Transport) as their engines land.

## Audio-library decision — RESOLVED (BASS/ManagedBass, 2026-06-05)

The realtime audio library is decided and the first vertical slice is built: `IAudioSource`
realtime playback, the audio frame pipeline (doc 02), and audio-driven beat detection (doc 03
realtime half) are all landed (see Realtime audio). What still remains to build on top:

- **Decks + mixer (doc 11):** two-deck playback, software mixer (crossfader/EQ/filter), hot cues,
  loops, beatmatching, multi-channel ASIO/CoreAudio cue output. Unblocked — just not built yet.
- **Capture sources:** system-loopback / sound-card input (doc 01 Phase 1b) — **first increment built**
  (`BassCaptureEngine` + `CaptureAudioSource`, see Realtime audio). Remaining: ASIO device pick and the
  Settings device-picker UI.
- **Decks + mixer (doc 11):** the software mixer's first increment is **built** (pure model + DSP
  math + `MixerActionHandler` + `BassMixer` routing skeleton; decks now slot-addressed — see Software
  mixer). Still to build: the two-deck BASS engine wiring channels into `BassMixer`, the master/cue
  bus into the frame pipeline, hot cues, loops, beatmatching/sync, and multi-channel ASIO/CoreAudio
  cue output.
- **Capture sources:** system-loopback / sound-card input (doc 01 Phase 1b) and ASIO device pick.
