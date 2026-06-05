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

`tests/Liveolator.Core.Tests` — **241 passing** (as of 2026-06-05).

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
  **`BeatActionHandler`** (see Beat) and **`PlaylistActionHandler`** (see Live playlist). Pending:
  Visual/Deck/Mixer/Transport handlers.

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
- **Deferred:** realtime `OnsetDetectionEngine` / audio `TempoEstimator` / `BeatTracker` /
  audio-driven `BeatClockService`. Blocker: the doc 02 audio frame pipeline, which needs an
  `IAudioSource` (gated on the audio-library decision). Also deferred: `BeatClockSource.External`
  (Ableton Link). Note: **offline** BPM/key analysis already exists under `Core/Analysis/`.

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
- **Deferred:** the concrete `IVisualPerformanceEngine` (drives the GPU compositor — lives in
  `Liveolator.Visuals`, blocked on the Silk.NET/OpenGL compositor) and a `VisualActionHandler`
  (the dispatcher bridge), which needs the engine + a scene/bank resolution policy that the real
  compositor will shape. Build the `VisualActionHandler` when the engine exists, mirroring
  `BeatActionHandler`.

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
  `Core/Library/` (incremental scan, music + visual catalogs), `Core/Playlist/`
  (`HarmonicSetBuilder`). Bindings: `Liveolator.Audio` (WAV + FFmpeg-CLI decode),
  `Liveolator.Platform` (file enumerator), `Liveolator.Visuals` (image/video probes only),
  `Liveolator.Media` (JSON catalog store), `Liveolator.Mcp` (music-intelligence server).

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

## Blocked until the audio-library decision (BASS vs PortAudio/miniaudio)

- `IAudioSource` realtime playback, decks + mixer (doc 11), the audio frame pipeline (doc 02),
  and therefore audio-driven beat detection (doc 03 realtime half).
