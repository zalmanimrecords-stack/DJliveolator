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

## Product direction — the differentiator (2026-06-03)

Liveolator is **deliberately not a maximalist pro-DJ tool.** It does not compete with
Serato / rekordbox / Traktor on DJ feature depth. Its uniqueness is the **tight coupling
between visuals and music, and controlling both at once.** The DJ engine exists to make
**beat sync and mixing effortless — near-automatic — so the performer's hands and attention
are freed to play the visuals.**

Consequences that bind the rest of the design:

1. **Effortless sync is a product requirement, not a feature.** One-button tempo sync that
   handles octave (½×/2×) ambiguity transparently, with phase alignment as a separate
   snap-to-beat control, plus opt-in **auto-mix / auto-transition** assist. The performer
   should never *babysit* the mix.
2. **One shared beat clock drives BOTH audio and visuals.** The Core beat engine exposes a
   single **Ableton-Link-style timeline** `(hostTime, beatTime, tempo)` plus a **quantum**
   for bar/phrase alignment. The DJ mix scheduler *and* the visual compositor read phase /
   beat from this one clock, so "control both simultaneously" and "beat-synced visuals"
   fall out by construction. Visual clip launches / parameter changes use **quantized
   launch** (snap to next beat/bar) — the visual analogue of audio quantize. Interop with
   **real Ableton Link** (sync to/from Ableton, Resolume, etc.) is an optional extension.
3. **`PerformanceAction` is the seam that unifies them** — one action can beat-sync audio
   and visuals together; every input (Push 1, CMD STUDIO 2A, UI, autopilot) feeds it.
4. **Autopilot (doc 10) on the DJ side is a core mechanism**, not a side feature — it is
   what frees the operator to focus on the visual performance.
5. **Explicitly out of scope:** AI stem separation, 4 decks, pro-FX maximalism. Liveolator
   stays **2 decks**.
6. **In scope because it serves "easy to mix":** musical key detection + Camelot
   harmonic-mixing hints (cheap at analysis time; lowers the skill needed to mix well).

Evidence base for these decisions:
`docs/research/dj-market-and-dsp-research.md` and
`docs/research/dj-gaps-keydetect-latency-automix-avsync.md`.

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
| 02, 06, 07, 09, 12, 13, 14, 15 | Carry over (UI doc 12: WPF→Avalonia wording only) |
| 00-architecture-overview | **Updated** (2026-06-03): multi-project Liveolator layout, compositor seam, Avalonia/Silk.NET, `IAudioDecoder` seam |
| 16 — track analysis & library | **New** (2026-06-03): folder scan + offline BPM/key/cues, `IAudioDecoder` seam (good first Core module) |
| 03 — beat engine | **Updated** (2026-06-03): added Link-style shared timeline + quantum, and key detection |
| 04 — performance actions | **Updated** (2026-06-03): visual actions → compositor model; added deck/mixer/sync/auto-mix actions + unified A/V timing |
| 08 — visual engine | **Replaced** (2026-06-03): projectM presets → texture/layer compositor + shared-clock coupling |
| 10 — autopilot | Carry over (now also the driver of DJ-side auto-mix, per Product direction) |
| 11 — decks | **Updated** (2026-06-03): Liveolator/cross-platform wording; Sync Lock + Quantize committed; Auto-Mix + harmonic hint added |
| 01 — audio source layer | **Partially revised** (2026-06-03): added latency targets + RT-thread rules, cross-platform wording. **Still pending:** NAudio types → chosen audio library (gated on the open audio-library decision) |
| 05 — controller mapping | **Revise (pending):** DryWetMidi → RtMidi/libremidi |

This context doc is the source of truth. Remaining queued revision: doc 05 (MIDI library)
and doc 01's final backend binding (both gated on open decisions above).
