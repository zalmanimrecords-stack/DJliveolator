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
| Audio frames | `IAudioFrameProvider` | Frame pipeline | Visual compositor + beat engine |
| Beat clock | `IBeatClock` / `IBeatTimeline` (emits `BeatClockState`) | Beat engine (DJ-side) | Visual + playlist + UI |
| Control | `IPerformanceActionDispatcher` | Mapping / UI / autopilot | All engines |
| Decode (offline) | `IAudioDecoder` | (backend, TBD) | Track-Analysis module (doc 16) |

> **The beat clock is the decoupling point for "visuals follow the DJ."** The DJ/beat
> engine is the *producer* of `IBeatClock`; the visual engine is a *consumer*. They never
> reference each other — only the seam — so the same clock can also come from an external
> Ableton Link session (doc 03). This is how the product differentiator (doc 00 context,
> "one shared beat clock") is realized without coupling modules.

## Project layout (Liveolator multi-project)

Per the Liveolator context doc, the code is split into separate .NET projects so the
platform-agnostic core stays free of UI and native dependencies and unit-tests without
hardware (global standards #2, #4, #5). Each top-level **module** below maps to the
modules described in planning.

```text
src/
  Liveolator.Core/        # platform-agnostic, no UI, no native — pure C#, fully unit-tested
    Frames/               # IAudioFrameProvider, AudioFrameData, FFT          (doc 02)
    Beat/                 # onset, tempo, tracker, grid, IBeatClock/Timeline  (doc 03)
    Key/                  # chroma/PCP, key classifier, Camelot               (doc 03)
    Analysis/             # Track-Analysis: scan + BPM/key/cues, IAudioDecoder (doc 16)
    Actions/              # PerformanceAction model + dispatcher               (doc 04)
    Mapping/              # mapping engine, learn, profiles (device-agnostic)  (doc 05)
    Mapping/Profiles/     # Push profile, DJ controller profile                (docs 06, 07)
    Playlist/             # HarmonicSetBuilder (Camelot sets) + library queue   (docs 09, 16)
    Library/              # MediaLibrary<T>, Music/Visual libraries, IFileEnumerator seam (doc 16)
    Autopilot/            # rule engine, scene pools                           (doc 10)
    VisualModel/          # VisualScene, Bank, Macro, Quantize (model only)    (doc 08)
    Persistence/          # profile + session + analysis-cache serialization   (doc 13)
  Liveolator.Audio/       # audio I/O binding: WavAudioDecoder, FfmpegAudioDecoder (CLI),
                          # CompositeAudioDecoder; decks output                 (docs 01, 11, 16)
  Liveolator.Media/       # filesystem IFileEnumerator, JsonCatalogStore (doc 13 cache),
                          # PlaylistWriter — the I/O + persistence binding      (docs 13, 16)
  Liveolator.Visuals/     # IVisualMediaProbe (ImageHeaderProbe + ffprobe video); Silk.NET
                          # compositor + GLSL + FFmpeg (compositor TBD)         (docs 08, 16)
  Liveolator.Midi/        # RtMidi/libremidi binding: IMidiInput/IMidiOutput    (doc 05)
  Liveolator.Mcp/         # MCP server exposing library/analysis/harmonic/playlist tools
  Liveolator.App/         # Avalonia UI; hosts modules, wires seams             (doc 12)
tests/                    # xUnit: Core.Tests (pure) + Media/Audio/Visuals/Integration (doc 14)
```

**Module ↔ seam wiring:** every module talks to the others *only* through the four seams
(`IAudioSource`, `IAudioFrameProvider`, `IBeatClock`, `IPerformanceActionDispatcher`) plus the
library seams (`IAudioDecoder`, `IFileEnumerator`, `IVisualMediaProbe`). The Push module
(docs 05/06) and the MIDI-mapping module (doc 05) emit `PerformanceAction`s; the visual module
(doc 08) consumes `IBeatClock`. No module references another module directly.

### Canonical bindings & consolidation (2026-06-04)

Parallel development produced duplicate seam implementations. The canonical choices (others to
be removed once `Liveolator.App` is closed so its output unlocks):

- **`IFileEnumerator` → `Liveolator.Media.FileSystemFileEnumerator`** (the established I/O +
  persistence home that the MCP server already depends on). `Liveolator.Platform.FileSystemEnumerator`
  and `Liveolator.App/Services` copies are redundant and should be removed; consumers point to Media.
- **`IAudioDecoder` → `Liveolator.Audio`** (`Composite` routing `WavAudioDecoder` + CLI `FfmpegAudioDecoder`).
  The `Liveolator.App/Services/WavAudioDecoder` copy is redundant; App references Audio.
- **`IVisualMediaProbe` → `Liveolator.Visuals.CompositeVisualMediaProbe`** (image header + ffprobe video).

Tests live in `tests/` over `Liveolator.Core` — see
[the test strategy and commands](core-business-logic/00-project-context.md).

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

Live audio capture, beat analysis, and the Avalonia/Silk.NET (OpenGL) render loop run on
different threads. The design isolates them with these rules (detailed per subsystem):

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

- Keeping `Liveolator.Core` free of UI/native deps is the load-bearing rule: if a native
  or Avalonia type ever leaks into Core, the no-hardware unit-test guarantee breaks. Core
  references nothing platform-specific; bindings (Audio/Midi/Visuals/App) depend on Core,
  never the reverse.
- The seam interfaces must be stable; churning them mid-roadmap forces rework in
  every dependent phase. They are reviewed and frozen at the end of Phase 0.
