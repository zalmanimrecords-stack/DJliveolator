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
