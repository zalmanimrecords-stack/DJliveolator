# 00 — Architecture Overview

## Purpose

Define the cross-cutting structure for Live Mode: the layers, the one-way data
flow, the project/folder layout, and naming conventions. Every other document in
this set conforms to what is decided here.

## The action-layer principle

The single most important rule: **inputs never wire directly to engines.**

```text
┌─────────────────────────────────────────────────────────────┐
│  Sources of intent                                           │
│  Hardware (Push, DJ ctrl)  │  UI buttons  │  Keyboard  │ Autopilot │
└───────────────┬─────────────────────────────────────────────┘
                │ raw MIDI / clicks / rules
                ▼
        ┌───────────────────────┐
        │  Controller Mapping   │  (MIDI/CC/rule → action)
        └───────────┬───────────┘
                    │ PerformanceAction (serializable command)
                    ▼
        ┌───────────────────────┐
        │  Action Dispatcher    │  (single entry point)
        └───────────┬───────────┘
        ┌───────────┼───────────┬───────────────┐
        ▼           ▼           ▼               ▼
   Audio Engine  Beat Engine  Visual Engine  Playlist Engine
        │           │           │               │
        └───────────┴─────┬─────┴───────────────┘
                          │ feedback state
                          ▼
                 LEDs / UI indicators
```

This means Push, DJ controllers, keyboard shortcuts, UI buttons, and autopilot all
emit the *same* `PerformanceAction` values, and all engines are driven only by the
dispatcher. See [04 — Performance action system](04-performance-action-system.md).

## Data-flow contracts (the seams)

The architecture rests on four narrow interfaces. Defining these in Phase 0 lets
every later phase proceed independently.

| Seam | Interface | Producer | Consumer |
|------|-----------|----------|----------|
| Audio input | `IAudioSource` | Deck / loopback | Frame pipeline |
| Audio frames | `IAudioFrameProvider` | Frame pipeline | projectM + beat engine |
| Beat clock | `IBeatClock` (emits `BeatClockState`) | Beat engine | Visual + playlist + UI |
| Control | `IPerformanceActionDispatcher` | Mapping / UI / autopilot | All engines |

## Proposed project layout

Live Mode is large enough to warrant its own namespace tree inside the existing
main app project (`MilkDropVisualizer.App`), keeping one responsibility per file
(global standard #2, #5). New top-level folder: `Live/`.

```text
MilkDropVisualizer.App/
  Live/
    Audio/            # IAudioSource implementations, ring buffer  (doc 01)
    Frames/           # IAudioFrameProvider, AudioFrameData, FFT   (doc 02)
    Beat/             # onset, tempo, tracker, grid, clock, tap    (doc 03)
    Actions/          # PerformanceAction model + dispatcher        (doc 04)
    Mapping/          # MIDI input/output, learn, profiles          (doc 05)
    Mapping/Profiles/ # Push profile, DJ controller profile         (docs 06, 07)
    Visual/           # VisualScene, Bank, Macro, Quantize           (doc 08)
    Playlist/         # Now/Next/Later live queue                    (doc 09)
    Autopilot/        # rule engine, scene pools                     (doc 10)
    Decks/            # Deck A/B, crossfader, mixer (Phase 10)       (doc 11)
    Persistence/      # profile + session serialization              (doc 13)
  UI.Analog/
    Modules/          # DJ Sync, Mappings, Scene Grid (new modules)  (doc 12)
```

Tests live in the existing test projects (`MilkDropVisualizer.App.Tests`,
`MilkDropVisualizer.App.UI.Analog.Tests`) — see
[14 — Testing and validation](14-testing-and-validation.md).

## Naming conventions

- Interfaces are role names: `IAudioSource`, `IBeatClock`, `IMidiInput`.
- Engines/services end in `Service` only when they own a lifecycle/loop
  (`BeatClockService`, `MidiInputService`). Pure computation ends in `Engine`
  (`OnsetDetectionEngine`) or `Estimator`/`Tracker`.
- Immutable snapshots are `record`s ending in `State` (`BeatClockState`) or
  carrying their domain noun (`AudioFrameData`, `TempoCandidate`).
- Serializable saved data ends in `Profile`, `Scene`, `Bank`, `RuleSet`,
  `Session`. See [13 — Data and persistence](13-data-and-persistence.md).

## Threading model

Live audio capture, beat analysis, and the WPF/OpenGL render loop run on different
threads. The design isolates them with these rules (detailed per subsystem):

- Capture thread writes only into a lock-free / single-producer ring buffer.
- Beat engine reads frames on its own analysis cadence and publishes an immutable
  `BeatClockState` (swap-a-reference, no shared mutable state).
- The render loop and UI read the latest `BeatClockState` snapshot — never block
  on capture or analysis.
- The action dispatcher marshals UI-affecting actions to the UI thread; engine
  actions are applied on the owning engine's thread.

This preserves the current "no UI freeze" behavior of the render loop.

## Feature-flag / rollout posture

Live Mode is a large, behavior-changing addition. Per global standard #24 it sits
behind a master **Live Mode** toggle. With the flag off, the app behaves exactly as
today (file playback + visualization). Each phase keeps existing playback and
visualization green (global standard #7).

## Phase

Phase 0. This document and the four seam interfaces are the Phase 0 deliverable —
added without changing existing behavior.

## Risks

- Putting all Live code in the main app keeps interop simple but grows the project.
  If it becomes unwieldy, the `Live/` tree can be extracted into a
  `MilkDropVisualizer.Live` class library later — the namespace boundaries above
  are chosen to make that extraction mechanical.
- The seam interfaces must be stable; churning them mid-roadmap forces rework in
  every dependent phase. They are reviewed and frozen at the end of Phase 0.
