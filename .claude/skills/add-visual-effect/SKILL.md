---
name: add-visual-effect
description: Add a GLSL visual effect (and its controllable parameters) to the Liveolator compositor — as an effect in a layer's chain, exposed through a VisualMacro/MacroTarget and driven via VisualSetMacro/VisualLaunchClip actions, beat-reactive off the shared clock. Use when adding a shader effect, a new layer source, a beat-synced visual, or a Push-knob-controllable visual parameter.
---

# Add a visual effect

The VJ engine is a **texture layer compositor with GLSL effects — no projectM/MilkDrop**
(settled decision, project `CLAUDE.md`). An effect is a GLSL fragment shader in a
layer's effect chain; its parameters are driven by **macros**, and the engine is reached
only through `PerformanceAction`s, locked to the **one shared beat clock**.

Authoritative design: [`docs/08`](../../../docs/08-visual-performance-engine.md) (effect
model, macro→parameter indirection, beat-sync) and
[`docs/03`](../../../docs/03-beat-engine.md) (the clock).

> **Current state:** `Liveolator.Visuals` today holds only media probes — the compositor
> and `IVisualPerformanceEngine` are not built yet (doc 08 Phase 3+). When the engine is
> still absent, this skill is the design contract to build against, not a wiring guide.

## The layering model (doc 08)

```
IVisualSource (image | video clip | camera) → texture
Layer = Source + Effect chain (GLSL) + Blend + Opacity
Compositor → output texture
```

Multiple layers render at once — a "scene" is a layer stack, not one preset.

## Steps

1. **Write the GLSL fragment shader** for the effect. On compile failure the engine must
   fall back to a pass-through effect and surface the error (doc 08) — don't crash the
   render loop.

2. **Declare the effect's parameters as a macro**, not as ad-hoc plumbing. Add a
   `VisualMacro(name, min, max, default, MacroTarget)`; the `MacroTarget` indirection maps
   `"echo"`/`"speed"`/… to the concrete shader uniform on its bound layer. Adding a macro
   is **data, not new control code** (doc 08).

3. **Make it beat-reactive (the differentiator), if relevant.** Read the latest immutable
   `BeatClockState` snapshot per frame — pulse on `IsBeat`/`IsDownbeat`, animate off
   `BeatPhase`/`BarPhase`, scale intensity by `Confidence`. Never block the render loop on
   analysis. Quantized changes (clip launch, scene/source swap) resolve their fire time
   through `IBeatScheduler` / `IBeatTimeline.NextBoundary(...)`, gated on a minimum beat
   confidence with an immediate fallback.

4. **Drive it only through actions.** The parameter is moved by `VisualSetMacro`
   (continuous, from a Push knob/UI/autopilot) and clips launch via `VisualLaunchClip`;
   these reach `IVisualPerformanceEngine` through the dispatcher. To add a *new* visual
   action kind, use the `add-performance-action` skill; to put it on a Push knob, use
   `add-controller-mapping`.

5. **Persist** scene/layer/macro definitions as JSON (doc 13). Layers reference sources by
   path/clip-id/camera-id; a **missing asset degrades to a transparent layer with a logged
   warning**, never a crash (doc 08, global standard #26).

## Guardrails

- No CPU effect kernels — Zalmanolator's kaleidoscope/echo/particles are the **algorithm
  reference to port to GLSL**, not reused as CPU code (doc 00/08).
- All native/GL/FFmpeg stays in `Liveolator.Visuals`; Core holds only the platform-agnostic
  scene model.
- Bound layer count + clip resolution — GPU budget is the real ceiling; keep it measured
  (doc 08/14).

## Validate

```powershell
dotnet build
dotnet test
```
Plus a visual smoke check once the compositor exists (A/V latency shares the beat-clock
metric, doc 14).
