# 00 — Liveolator Context (read this first)

This document captures **why Liveolator exists** and **what carries over** from the
Zalmanolator "Live Mode" design, so the rest of the docs can be read with the right
lens. It is the authoritative statement of direction; where a numbered design doc
still says "projectM", "WPF", "NAudio/ASIO-only", or "DryWetMidi", this document
supersedes it (those docs are being revised — see the status table below).

## Why a new project

Zalmanolator is Windows-locked at four layers — **WPF** (UI), **C++/CLI**
(projectM interop), **NAudio + ASIO** (audio), **WGL** (graphics) — none of which run
on macOS. The user requires:

1. **macOS support — a hard, near-term requirement.**
2. **Distribution to other users** (cross-platform installers, broad hardware support).
3. The core value is **Live Mode (DJ + VJ)**, which was only ever design — so it is
   greenfield regardless. The mature Zalmanolator features (export, overlays) are
   deprioritized.

Because the valuable part is unbuilt and the Windows layers cannot be ported,
continuing Zalmanolator cannot meet the requirement. Liveolator is a fresh,
cross-platform .NET project that **reuses the design and the C# algorithms**, not the
Windows-bound implementations.

## Product definition

A cross-platform **DJ + VJ** application controlled from Push 1 + CMD STUDIO 2A:

- **DJ:** two decks, software mixer (crossfader, per-channel EQ/filter), beat engine,
  low-latency audio, headphone cue (via the CMD STUDIO 2A's built-in interface).
- **VJ:** real-time, GPU-shader manipulation of **images + video clips + live
  camera/capture**, composited in layers, beat-synced. **MilkDrop/projectM is dropped
  entirely.**

## The visual engine — reimagined (replaces doc 08)

A **texture-based layer compositor**, not a preset player:

```text
IVisualSource → a GPU texture per frame
  ├─ ImageVisualSource      (still image → texture)
  ├─ VideoClipVisualSource  (FFmpeg decode → stream of textures; play/loop/scrub/speed)
  └─ CameraVisualSource     (webcam / capture device → stream of textures)
        ↓
Layer = Source + Effect chain (GLSL fragment shaders) + Blend mode + Opacity
        ↓
Compositor → output texture → display/output window + capture for stream/record
```

- **Effects** are GLSL shaders (kaleidoscope, glitch, echo/feedback, blur,
  displacement, color), parameterized by `VisualMacro` (Push knobs) and
  `BeatClockState` (beat/bar phase).
- The design concepts from doc 08 are **kept**: `VisualScene`, `VisualBank`,
  `VisualMacro`, `VisualQuantize`. Only the content behind a scene changes — a scene is
  now a set of layers (sources + effects + blend), not a projectM preset.
- Zalmanolator's existing CPU effect kernels (`KaleidoscopeKernel`, echo, particles)
  are an **algorithm reference** to port to GLSL — not reused as-is (CPU byte buffers
  don't scale to 60fps video).

## What carries over from `docs/` (the Zalmanolator Live design)

The seam architecture (doc 00) is platform- and visual-engine-agnostic and is the
backbone of Liveolator:

- **Carries over 1:1:** the four seams (`IAudioSource`, `IAudioFrameProvider`,
  `IBeatClock`, `IPerformanceActionDispatcher`), the beat engine (doc 03), the
  performance action system (doc 04), controller mapping concepts (doc 05), Push 1 /
  CMD STUDIO 2A profiles (docs 06/07), the live playlist (doc 09), autopilot (doc 10),
  decks/mixer (doc 11), persistence (doc 13), testing approach (doc 14), and the phased
  delivery idea (doc 15).
- **Ports from C# algorithms:** FFT/spectrum, BPM logic, effect math (→ GLSL).

## Cross-platform stack

| Concern | Choice | Replaces (Zalmanolator) |
|---------|--------|--------------------------|
| Runtime / UI | .NET 8 + **Avalonia** | WPF |
| Graphics / effects | **OpenGL via Silk.NET** + GLSL | C++/CLI + WGL + projectM |
| Video decode | **FFmpeg** (→ GL texture); libVLC alt | — (new) |
| Camera / capture | FFmpeg (dshow/avfoundation) / OpenCV | — (new) |
| Audio I/O | **OPEN DECISION** — see below | NAudio + ASIO |
| MIDI | **RtMidi / libremidi** | DryWetMidi |

ASIO (Windows) / CoreAudio (Mac) are reached through the audio library; app code sees
only `IAudioSource` / the output seam.

## Open decisions

1. **Audio library — not yet chosen:**
   - **BASS / ManagedBass** — easiest for a DJ app (decode, mix, tempo/pitch, ASIO,
     CoreAudio), mature, widely used in DJ software. **Requires a paid commercial
     license for distribution.**
   - **PortAudio / miniaudio (open)** — free and flexible; we implement tempo/pitch and
     mixing ourselves on top.
   - This choice affects doc 01 and doc 11; pick before writing the audio binding.

2. Autopilot override default = auto-resume after a window (already decided, doc 10).

## Doc revision status

| Doc | Status for Liveolator |
|-----|------------------------|
| 00, 02, 03, 04, 06, 07, 09, 10, 12, 13, 14, 15 | Carry over (UI doc 12: WPF→Avalonia wording only) |
| 01 — audio source layer | **Revise:** NAudio/ASIO → audio-lib + PortAudio/CoreAudio |
| 05 — controller mapping | **Revise:** DryWetMidi → RtMidi/libremidi |
| 08 — visual engine | **Replace:** projectM presets → texture/layer compositor (see above) |
| 11 — decks | **Revise:** output routing via cross-platform audio lib |

These revisions are queued; this context doc is the source of truth until they land.
