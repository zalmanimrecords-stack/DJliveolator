# Liveolator.Core — module rules

**Purpose:** the platform-agnostic brain. Performance actions + dispatcher, beat
engine, controller mapping, playlist, autopilot, DSP/analysis, and the library/visual
scene models. No UI, no native, no hardware.

**Design source of truth:** [`docs/03`](../../docs/03-beat-engine.md) ·
[`docs/04`](../../docs/04-performance-action-system.md) ·
[`docs/05`](../../docs/05-controller-mapping-engine.md) ·
[`docs/09`](../../docs/09-live-playlist-engine.md) ·
[`docs/10`](../../docs/10-autopilot-show-rules.md) ·
[`docs/16`](../../docs/16-track-analysis-library.md)

## Iron rules

1. **Pure C# only** — no Avalonia/UI, no OpenGL/native, no platform-specific IO.
   Everything here must unit-test under xUnit with no hardware present. This boundary
   is non-negotiable (project `CLAUDE.md`).
2. **Engines are driven only through `PerformanceAction` + the dispatcher** (doc 04).
   No source-to-engine or engine-to-engine direct calls.
3. **Seams live here as interfaces** (`IAudioDecoder`, `IFileEnumerator`, …); the
   concrete platform/native implementations live in Audio / Media / Platform / Visuals.
4. **One handler per concern, small focused files** — no giant switch (doc 04,
   global standards #2/#3).

**Tests:** `tests/Liveolator.Core.Tests`.
