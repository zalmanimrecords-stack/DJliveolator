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

The GL compositor lives in `Gl/`:

- `FrameUniforms` — **pure**, GL-free: resolves the brightness macro + `BeatClockState` into the
  shader's per-frame uniforms (brightness + confidence-gated beat flash + blackout). Unit-tested.
- `RgbaImage` / `SkiaImageLoader` — decode a still image to RGBA8 pixels for the layer texture
  (SkiaSharp, managed/cross-platform). A bad/missing file → `ImageLoadException`.
- `SceneComposition` / `ResolvedLayer` — **pure**: resolve a `VisualScene`'s layer stack into the
  ordered (bottom→top) draw list, carrying each layer's blend mode + opacity + renderability (image
  layers render; video/camera resolve non-renderable and are skipped). Unit-tested.
- `BlendModeGl` — **pure**: maps `BlendMode` → premultiplied-alpha fixed-function GL blend factors
  (Normal/Add/Screen/Multiply). `Overlay` is non-separable (no fixed-function mapping) and degrades
  to Normal with a warning. Unit-tested.
- `LiveClockSelector` — **pure**: chooses the clock the visuals bind to — the audio-driven master
  clock when realtime audio is up, else the manual tap clock. Unit-tested.
- `LayeredQuadShaderSource` / `LayeredQuadRenderer` — the fullscreen-quad GLSL program and the
  multi-layer renderer: one texture per layer, drawn bottom→top with per-layer opacity + blend state.
  A single image layer reproduces the original single-layer slice. Needs a current GL context
  (created in `Run`).
- `GlVisualPerformanceEngine : IVisualPerformanceEngine` — `SetMacro`/`Blackout`/`ActiveBank`/
  `CurrentFrame`/`CurrentComposition` are pure observable state (unit-tested off the GPU); `Run()`
  opens the window and renders the active scene's blended layer stack. Video/camera sources,
  quantized launch, and transitions are **deferred** logged no-ops — they grow into this class.

**Manual visual verification (no headless path — GL needs a display):**

1. Construct a `VisualBank` whose first scene's first layer is a `VisualSourceKind.Image` pointing
   at a real image file.
2. `new GlVisualPerformanceEngine(bank, brightnessMacro, beatClock).Run();` — a window shows the
   image as a fullscreen quad.
3. Drive `SetMacro("brightness", v)` (0..1) → the image dims/brightens.
4. Feed a beat clock with `IsBeat=true` + `Confidence>0` → the image flashes on the beat.
5. `Blackout(true)` → output goes black; `Blackout(false)` restores.
6. **Multi-layer + blend:** give the scene a second image layer (`BlendMode.Add` or `Screen`,
   `Opacity < 1`). The two images composite — Add/Screen lighten where they overlap, Multiply
   darkens — and lowering the top layer's opacity fades its contribution. With one layer the output
   is identical to step 2.
7. **Live-clock binding:** with realtime audio up (BASS present), the window pulses on the audible
   master beat; headless, it pulses on the Live tab tap clock. (Composition picks the clock via
   `LiveClockSelector` in `ServiceConfig.WireVisuals`.)

**App wiring:** `ServiceConfig.WireVisuals` registers the engine as `IVisualPerformanceEngine`, joins
its `VisualActionHandler` to the one dispatcher, and binds the engine to the `LiveClockSelector`-chosen
clock. `Run()` is launched on demand via `IVisualStage` (the RENDER-WINDOW SEAM) — never at
composition, so the app stays headless-safe.
