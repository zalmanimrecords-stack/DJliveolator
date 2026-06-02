# 14 — Testing and Validation

## Purpose

Define how Live Mode is tested. Per the global standards, every meaningful change is
covered by tests written before/with implementation (TDD, global standards #1, #8),
and nothing is claimed done without running them (#9).

## Existing test infrastructure (verified)

- `MilkDropVisualizer.App.Tests` — `OutputType=Exe`, a **manual test runner**
  (`Program.cs`). It references the main app and therefore pulls projectM/native
  artifacts. Good for integration-style checks that need the app; not a unit harness.
- `MilkDropVisualizer.App.UI.Analog.Tests` — **xUnit** (`Microsoft.NET.Test.Sdk`
  17.8.0, `xunit` 2.6.6). Per its own csproj comment it "builds & tests pure-logic
  types only (VMs, adapters, helpers)" and produces **no** projectM/native
  artifacts.

### Consequence for the design

The Live subsystems are deliberately built as **pure-logic, native-free types**
(onset/tempo/tracker/grid, action model + dispatcher, mapping engine, live-playlist
queue, autopilot rules). These are unit-tested in the **xUnit** project (or a new
sibling pure-logic test project of the same shape) with no audio device, no MIDI
device, and no GL context. The interfaces in docs 01–10 are designed for exactly this
— engines depend on data (`AudioFrameData`, `BeatClockState`, `MidiMessage`,
`PerformanceAction`), not hardware.

## Automated tests (from the plan + standards)

### Beat engine (doc 03)

- **Synthetic click tracks** at 80, 100, 120, 128, 140, 174 BPM → assert detected
  BPM within tolerance and rising confidence.
- **Half/double cases**: 70/140, 87/174 → assert the candidate list contains both and
  the lock honors the performer's choice.
- **Silence / low-volume** → assert no false lock; confidence near zero.
- **Sudden tempo change** → assert re-lock behavior and that `IsLocked` prevents
  jitter while locked.
- Tap-tempo, ÷2, ×2, nudge, reset-grid, set-downbeat → deterministic state
  transitions.

Click tracks are generated in-test (impulse trains at known intervals) and fed
through the real `OnsetDetectionEngine`/`TempoEstimator` as `AudioFrameData` — no
audio device required.

### Action dispatcher (doc 04)

- `Dispatch(kind)` routes to exactly the right handler, once, with the right
  value/slot.
- Quantized kinds defer through a fake `IBeatScheduler` and fire on the scheduled
  boundary.
- Feedback state updates and `FeedbackChanged` fires.

### Controller mapping (doc 05)

- A `MidiMessage` matching a `ControllerBinding` produces the expected
  `PerformanceAction` (absolute/relative/momentary/toggle conversions, curves).
- Learn mode infers the right `InputMode` from a captured message; user override
  wins.
- Conflict detection flags duplicate `(type, channel, data1)` bindings.

### Live playlist (doc 09)

- **Queue mutation while "playing"**: reorder/insert/remove on `Upcoming` never
  changes `Now`.
- `RemoveFuture` refuses to remove `Now`.
- `SkipOn(NextBar)` schedules via a fake beat clock and fires on the bar boundary.
- Auto-advance transitions `Now` → next correctly.

### Persistence (doc 13)

- Round-trip serialize/deserialize for every persisted type.
- Reanalysis **skips** a `BeatGrid` flagged `IsManual` (the sacred-edit rule).
- Corrupt file → backed up + default loaded, no throw.
- Schema-version migration loads older files.

### Pipeline regression (doc 02)

- The `SpectrumAnalyzer` extraction produces numerically identical spectrum/waveform
  to the pre-refactor `AudioAnalyzer` for a fixed input buffer (behavior-preservation
  gate before old code is deleted).

## Manual tests (require hardware / real audio)

Run via the `App.Tests` runner or by hand; documented as a checklist:

- Spotify through system loopback drives projectM.
- YouTube (browser) through loopback.
- VLC / local player through loopback.
- Internal deck playback still works (Live Mode off and on).
- Push pad triggers a visual scene; knob moves a macro.
- Generic MIDI controller learn-maps an action.
- DJ controller transport mapping operates the single-deck workflow.

## Performance metrics (measured, from the plan)

- Audio-capture latency.
- Beat-event latency (true beat → `IsBeat`).
- UI responsiveness during capture (no freeze — doc 00 guarantee).
- CPU usage with projectM + beat engine + MIDI input running together.
- Memory growth over a long set (catches preload/ring-buffer leaks — doc 09).

These are captured during manual sessions and recorded against the success criteria
of each phase (doc 15).

## Validation gate per phase

Before any phase is called done (global standard #9): build all projects, run the
xUnit suite, run the manual checklist items relevant to that phase, and confirm
existing file playback + visualization still work with Live Mode off.

## Risks

- True end-to-end latency can only be measured on real hardware; unit tests bound
  correctness, not latency.
- Click-track synthetic onsets are cleaner than real music; supplement with a few
  recorded real-track manual checks per genre.
