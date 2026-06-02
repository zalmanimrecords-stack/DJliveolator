# 08 — Visual Performance Engine

## Purpose

Define visual behavior *above* raw projectM preset loading: named scenes, banks,
continuous macros, and beat-quantized launching. This is what Push pads and
autopilot actually trigger.

## Existing code this touches

- `MilkDropVisualizer.App/Visualization/ProjectMVisualizerHost.xaml.cs` — owns the
  OpenGL context, preset playlist, preset switching (hard/soft cut, duration,
  shuffle), mesh size, and the animation-speed multiplier. The visual engine drives
  *this* host; it does not bypass it.
- `ProjectMWrapper/ProjectMHost.cpp` — C++/CLI preset playlist + switching. Reused
  as-is via the existing host API.
- `MilkDropVisualizer.App/OverlayFxPanel.xaml.cs` and the overlay/echo/particle
  engines in `UI.Analog` — overlays become macro-addressable layers.
- `MilkDropVisualizer.App/Helpers/BeatDetectorService.cs` consumers — migrate to
  `BeatClockState` (doc 03).

## New concepts

```csharp
public sealed record VisualScene(
    string Name,
    PresetSelection Preset,        // single preset OR a preset pool
    IReadOnlyList<OverlayState> Overlays,
    IReadOnlyDictionary<string, double> MacroValues,
    TransitionStyle Transition,
    BeatBehavior BeatBehavior);    // preset-switch cadence, pulse targets

public sealed record VisualBank(string Name, IReadOnlyList<VisualScene> Scenes);

public sealed record VisualMacro(
    string Name,                   // intensity, speed, echo, particles, ...
    double Min, double Max, double Default,
    MacroTarget Target);           // what engine parameter it drives

public enum Quantize { Immediate, NextBeat, NextBar, EveryNBars }  // shared w/ doc 03
```

- **VisualScene** — a saved combination of preset(s), overlays, macro values,
  transition style, and beat behavior. Loading a scene applies all of it atomically.
- **VisualBank** — a group of scenes mapped to Push pads / the Scene Grid (doc 12).
- **VisualMacro** — a named continuous parameter driven by MIDI knobs (doc 06), UI
  sliders, or autopilot. Macros map to concrete targets: projectM animation speed,
  overlay scale/opacity, echo amount, particle density, kaleidoscope, etc.
- **VisualQuantize** — when an action takes effect, via `IBeatScheduler` (doc 03).

## The visual engine

```csharp
public interface IVisualPerformanceEngine
{
    void LoadScene(VisualScene scene, Quantize when, int everyN = 1);
    void SetMacro(string name, double value);    // 0..1 normalized
    void ToggleOverlay(int layer);
    void Blackout(bool on);
    void Strobe(bool on);
    void Transition(TransitionStyle style, Quantize when, int everyN = 1);
    VisualBank ActiveBank { get; }
    void SelectBank(int index);
}
```

The engine receives only `PerformanceAction`s from the dispatcher (doc 04). It
translates them into projectM host calls and overlay-engine settings, deferring
quantized actions through the beat scheduler.

## Beat-sync targets (from the plan)

- Preset switching every 4 / 8 / 16 / 32 beats (per scene `BeatBehavior`).
- Overlay pulses on beat (`IsBeat` / `IsDownbeat` from `BeatClockState`).
- Image motion and echo driven by `BeatPhase` / `BarPhase`.
- Transition intensity from beat confidence or energy.
- Blackout and strobe as explicit performance actions (not automatic).

These read the latest `BeatClockState` snapshot; they never block on analysis.

## Macro → parameter mapping

A `MacroTarget` indirection keeps the engine decoupled from concrete widgets:
setting macro `"echo"` writes the overlay echo engine's amount; `"speed"` writes the
projectM animation-speed multiplier already exposed by the host. Adding a macro is
data, not new control plumbing.

## Persistence

`VisualScene`, `VisualBank`, and `VisualMacro` definitions are JSON-serialized under
the Live persistence root (doc 13). Scenes reference presets by path/id so they
survive preset-folder changes where possible.

## Error handling & logging

- All projectM host calls must run with the GL context current
  (`EnsureGLContextCurrent` in the host) — the engine routes through the host's
  existing safe entry points and logs interop failures with the scene/preset name.
- A missing preset referenced by a scene logs a warning and falls back to the
  current preset rather than crashing the render loop (global standard #26).

## Phase

- Phase 3: beat-clock integration into overlays/preset timing + the `Quantize`
  helper + beat/bar-aware preset switching.
- Phase 6+: scenes/banks/macros become Push- and Scene-Grid-addressable.

## Risks

- projectM supports one active preset at a time; "scene = preset pool + overlays"
  must not imply simultaneous presets (no preset stacking today). Transitions are
  scheduled switches, not layered renders.
- Quantized scene launches feel wrong if the beat clock is unstable; gate quantized
  visual launches on a minimum confidence, falling back to immediate.
