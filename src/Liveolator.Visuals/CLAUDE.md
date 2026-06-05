# Liveolator.Visuals — module rules

**Purpose:** the VJ engine — a Silk.NET/OpenGL compositor with GLSL effects, FFmpeg
video decode, and image/video media probes.

**Design source of truth:** [`docs/08`](../../docs/08-visual-performance-engine.md).

## Iron rules

1. **Image + video + camera compositor with GLSL ONLY. NO projectM/MilkDrop.** This is
   a settled key decision (project `CLAUDE.md`).
2. **The visual engine is driven only through `PerformanceAction`** (the visual handler,
   doc 04). No direct calls from UI or MIDI into the compositor.
3. **All native / GL / FFmpeg lives here, never in Core.** Core holds only the
   platform-agnostic visual scene model.

**Tests:** `tests/Liveolator.Visuals.Tests`.

## Compositor slice (doc 08, first vertical slice — `Gl/`)

The first GL compositor increment lives in `Gl/`:

- `FrameUniforms` — **pure**, GL-free: resolves the brightness macro + `BeatClockState` into the
  shader's per-frame uniforms (brightness + confidence-gated beat flash + blackout). Unit-tested.
- `RgbaImage` / `SkiaImageLoader` — decode a still image to RGBA8 pixels for the layer texture
  (SkiaSharp, managed/cross-platform). A bad/missing file → `ImageLoadException`.
- `QuadShaderSource` / `QuadRenderer` — one fullscreen textured quad through a GLSL fragment shader
  with the one brightness/strobe effect. Needs a current GL context (created in `Run`).
- `GlVisualPerformanceEngine : IVisualPerformanceEngine` — `SetMacro`/`Blackout`/`ActiveBank`/
  `CurrentFrame` are pure observable state (unit-tested off the GPU); `Run()` opens the window and
  renders. Layer-chain/blend, video/camera, quantized launch, transitions are **deferred** logged
  no-ops — they grow into this class.

**Manual visual verification (no headless path — GL needs a display):**

1. Construct a `VisualBank` whose first scene's first layer is a `VisualSourceKind.Image` pointing
   at a real image file.
2. `new GlVisualPerformanceEngine(bank, brightnessMacro, beatClock).Run();` — a window shows the
   image as a fullscreen quad.
3. Drive `SetMacro("brightness", v)` (0..1) → the image dims/brightens.
4. Feed a beat clock with `IsBeat=true` + `Confidence>0` → the image flashes on the beat.
5. `Blackout(true)` → output goes black; `Blackout(false)` restores.

**Not yet wired into the app:** `ServiceConfig` is untouched — the engine reaches the dispatcher
only once a `VisualActionHandler` exists (deferred, mirror `BeatActionHandler`).
