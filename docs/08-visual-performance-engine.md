# 08 — Visual Performance Engine

> **Replaces the projectM-era version.** Per `docs/00`, MilkDrop/projectM is dropped
> entirely; the visual engine is now a **texture-based layer compositor** (image + video
> clip + live camera, composited in GPU layers with GLSL effects). This doc defines the
> performance layer *above* the compositor: named scenes, banks, continuous macros, and
> **beat-quantized launching driven by the one shared beat clock** (doc 03). It is what
> Push pads and autopilot actually trigger.

> **✅ Status (2026-06-05): scene model BUILT in `Liveolator.Core/Visuals/`** — see
> [`18-implementation-status.md`](18-implementation-status.md). Implemented and tested: the
> vocabulary (`VisualScene`/`VisualLayer`/`VisualBank`/`VisualSourceRef`/`EffectRef`/
> `BeatBehavior`, `BlendMode`/`TransitionStyle`/`VisualSourceKind`), `VisualMacro` + `MacroTarget`
> with normalized→range resolution, the `IVisualPerformanceEngine` seam, and the shared
> confidence-gated `Beat.QuantizedLaunch` (visuals reuse the **same** `Quantize`/clock as audio).
> Also BUILT: the first GL compositor slice (`Liveolator.Visuals/Gl/`) and the
> `VisualActionHandler` dispatcher bridge (`Liveolator.Core/Visuals/`, mirrors `BeatActionHandler`,
> app-wired in `ServiceConfig.WireVisuals`) — see [`18`](18-implementation-status.md). **Pending:**
> the full layer/effect chain + video/camera sources in the engine, `VisualSetLayerSource` (no action
> payload yet), and launching the GL render window on demand. **Don't rebuild the scene/macro model,
> redefine `Quantize`, or re-add the handler.**

## Purpose

Give the performer high-level, beat-locked control of the visuals — load a scene, drive a
macro, launch a clip on the next bar — without touching the low-level compositor. This is
**the visual half of the product differentiator** (doc 00): the *same* beat clock that
drives the DJ mix drives the visuals, so audio and visuals stay locked by construction.

## The compositor it sits on (recap from doc 00)

```text
IVisualSource → a GPU texture per frame
  ├─ ImageVisualSource      (still image → texture)
  ├─ VideoClipVisualSource  (FFmpeg decode → textures; play/loop/scrub/speed)
  └─ CameraVisualSource     (webcam / capture → textures)
        ↓
Layer = Source + Effect chain (GLSL fragment shaders) + Blend mode + Opacity
        ↓
Compositor → output texture → display/output window + capture for stream/record
```

The performance engine **never** bypasses the compositor; it sets layer sources, effect
parameters, blend/opacity, and schedules changes. Unlike projectM, **multiple layers
render simultaneously** — a "scene" is a real layer stack, not a single preset.

## New concepts

```csharp
public sealed record VisualScene(
    string Name,
    IReadOnlyList<VisualLayer> Layers,          // ordered bottom→top; sources+effects+blend
    IReadOnlyDictionary<string, double> MacroValues,
    TransitionStyle Transition,                  // how we move INTO this scene
    BeatBehavior BeatBehavior);                  // per-layer beat reactivity / launch cadence

public sealed record VisualLayer(
    string Name,
    VisualSourceRef Source,                      // image path / video clip / camera id
    IReadOnlyList<EffectRef> Effects,            // GLSL effect chain + param defaults
    BlendMode Blend,
    double Opacity);

public sealed record VisualBank(string Name, IReadOnlyList<VisualScene> Scenes);

public sealed record VisualMacro(
    string Name,                                 // intensity, speed, echo, kaleidoscope, ...
    double Min, double Max, double Default,
    MacroTarget Target);                         // which layer/effect parameter it drives

public enum Quantize { Immediate, NextBeat, NextBar, EveryNBars }  // shared w/ doc 03
```

- **VisualScene** — a saved layer stack + macro values + transition + beat behavior.
  Loading a scene applies all of it atomically (next quantum boundary if quantized).
- **VisualLayer** — one composited layer: a source, a GLSL effect chain, blend mode, and
  opacity. Layers stack and blend; there is no "one active preset" limit.
- **VisualBank** — a group of scenes mapped to Push pads / the Scene Grid (doc 12).
- **VisualMacro** — a named continuous parameter driven by Push knobs (doc 06), UI sliders,
  or autopilot, mapped to a concrete layer/effect target.
- **VisualQuantize** — *when* an action takes effect, resolved via `IBeatScheduler` (doc 03).

## The visual engine

```csharp
public interface IVisualPerformanceEngine
{
    void LoadScene(VisualScene scene, Quantize when, int everyN = 1);
    void SetMacro(string name, double value);          // 0..1 normalized
    void SetLayerSource(int layer, VisualSourceRef source, Quantize when, int everyN = 1);
    void ToggleLayer(int layer);
    void SetLayerOpacity(int layer, double opacity);
    void LaunchClip(int layer, string clipId, Quantize when, int everyN = 1);  // video clip
    void Blackout(bool on);
    void Strobe(bool on);
    void Transition(TransitionStyle style, Quantize when, int everyN = 1);
    VisualBank ActiveBank { get; }
    void SelectBank(int index);
}
```

The engine receives only `PerformanceAction`s from the dispatcher (doc 04). It translates
them into compositor calls (layer source swaps, effect-parameter writes, blend/opacity) and
defers quantized actions through the beat scheduler.

## Beat-sync — reading the one shared clock (the differentiator)

The visual engine binds to the **same** beat clock as the DJ engine (doc 03). Two coupling
mechanisms:

1. **Per-frame reactive parameters** — each frame, effects read the latest immutable
   `BeatClockState` snapshot (never blocking on analysis):
   - Layer/effect **pulses** on `IsBeat` / `IsDownbeat`.
   - Continuous motion (echo feedback, displacement, kaleidoscope angle) driven by
     `BeatPhase` / `BarPhase`.
   - Transition or effect **intensity** scaled by beat `Confidence` or energy.
2. **Quantized launch** — scene loads, clip launches, layer-source swaps, and transitions
   resolve their fire time through `IBeatScheduler` / `IBeatTimeline.NextBoundary(...)`
   (doc 03), snapping to the next beat/bar/phrase. This is the visual analogue of the audio
   **Quantize** control — the same `quantum` concept, the same clock.

> Because both engines read one `IBeatTimeline`, syncing the visuals to the music is not a
> separate feature to maintain — it is the default. When the clock source is an external
> **Ableton Link** session (doc 03), the visuals lock to Ableton/Resolume/etc. too.

`BeatBehavior` on a scene/layer configures cadence (e.g. swap clip every 4/8/16 bars) and
which effect parameters pulse — all expressed against the shared clock.

## Macro → parameter mapping

A `MacroTarget` indirection keeps the engine decoupled from concrete shaders: setting macro
`"echo"` writes the echo effect's feedback amount on its bound layer; `"speed"` writes a
video clip's playback rate or an effect's animation rate. Adding a macro is data, not new
control plumbing. (Zalmanolator's CPU effect kernels — kaleidoscope, echo, particles — are
the **algorithm reference** to port to GLSL, per doc 00; they are not reused as CPU code.)

## Persistence

`VisualScene`, `VisualLayer`, `VisualBank`, and `VisualMacro` definitions are JSON-serialized
under the Live persistence root (doc 13). Layers reference sources by path/clip-id/camera-id
so scenes survive asset-folder changes where possible (a missing asset degrades gracefully —
see below).

## Error handling & logging

- All compositor/GL calls run with the GL context current; the engine routes through the
  compositor's safe entry points and logs GL/interop failures with the scene/layer name.
- A **missing asset** (image/clip not found, camera unavailable) referenced by a scene logs
  a warning and renders that layer as transparent/blacked rather than crashing the render
  loop (global standard #26).
- A failed shader compile falls back to a pass-through effect and surfaces the error.

## Phase

- Phase 3: shared-clock integration into the compositor (reactive params + the `Quantize`
  helper + beat/bar-aware clip launching and scene transitions).
- Phase 6+: scenes/banks/macros become Push- and Scene-Grid-addressable.

## Risks

- **Quantized launches feel wrong if the beat clock is unstable** — gate quantized visual
  launches on a minimum beat `Confidence`, falling back to immediate (same guard the audio
  Quantize uses).
- **GPU budget**: multiple simultaneous layers + video decode + effects at 60 fps is the
  real cost ceiling (unlike single-preset projectM). Layer count and clip resolution must be
  bounded and measured (doc 14).
- **A/V latency**: the gap between a true beat and the visual reacting must be measured; it
  shares the beat-clock latency metric with the audio side (doc 14).
