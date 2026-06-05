# Liveolator — Project Context for Claude

> Global development standards (TDD, small focused files, layer separation, error
> handling, type safety, etc.) from the user's global `~/.claude/CLAUDE.md` apply here
> in full. This file adds project-specific context.

## What Liveolator is

A **cross-platform (Windows + macOS) DJ + VJ performance application**, distributed to
users, controlled from **Ableton Push 1** (visuals) + **Behringer CMD STUDIO 2A** (DJ).

- **DJ engine:** two decks, software mixer (crossfader, per-channel EQ/filter), beat
  detection, low-latency audio, headphone cue (CMD STUDIO 2A built-in interface).
- **VJ engine:** real-time, GPU-shader manipulation of **images + video clips + live
  camera/capture input**, composited in layers, beat-synced (Resolume-style).
  **No MilkDrop/projectM.**
- **One action layer:** hardware, UI, and automation emit the same serializable
  `PerformanceAction`s; engines are driven only via a dispatcher (the seam architecture).

## History / why this project exists

Liveolator is the cross-platform successor to **Zalmanolator** (at
`../Zalmanolator`), which is Windows-locked (WPF + C++/CLI + NAudio + WGL) and cannot
run on Mac. The user needs Mac support (hard requirement) and distribution. The core
value (DJ+VJ "Live Mode") was only ever design docs in Zalmanolator, so it is
greenfield. We reuse the **design** and the **pure C# algorithms**, not the
Windows-bound implementations. Full rationale: `docs/00-LIVEOLATOR-CONTEXT.md`.

## Stack

| Concern | Choice |
|---------|--------|
| Runtime / UI | .NET 8 + **Avalonia** |
| Graphics / effects | **OpenGL via Silk.NET** + GLSL fragment shaders |
| Video decode | **FFmpeg** (frame → GL texture); libVLC as alternative |
| Camera / capture | FFmpeg (dshow on Win / avfoundation on Mac) or OpenCV |
| Audio I/O | **OPEN DECISION — not yet chosen** (see below) |
| MIDI | **RtMidi / libremidi** (cross-platform) |

ASIO (Windows) / CoreAudio (Mac) are reached through the audio library; app code only
sees the platform-agnostic `IAudioSource` seam.

## Planned layout

```text
src/Liveolator.Core/      # platform-agnostic: seams, beat engine, actions, mapping,
                          # playlist, autopilot, visual scene model (no UI, no native)
src/Liveolator.App/       # Avalonia UI
src/Liveolator.Audio/     # audio I/O binding + offline decode (Wav managed / FFmpeg CLI)
src/Liveolator.Media/     # filesystem enumerator + JSON catalog cache (doc 13)
src/Liveolator.Mcp/       # MCP server: music-intelligence tools for external AI agents (doc 17)
src/Liveolator.Midi/      # MIDI binding
src/Liveolator.Visuals/   # Silk.NET/OpenGL compositor + shaders + FFmpeg decode
tests/                    # xUnit tests for Core (pure logic, no native)
docs/                     # architecture & design
```

## Key decisions (made)

- Cross-platform new project (not extending Zalmanolator).
- Drop projectM/MilkDrop entirely; visual engine = image + video clip + camera
  compositor with GLSL effects.
- Hardware: Push 1 + CMD STUDIO 2A (both class-compliant, work on Win + Mac).
- Push 1 LED model: pad LEDs via NoteOn(velocity=color, channel=blink), buttons via
  CC, LCD/mode via SysEx; requires Push **User mode**.
- CMD STUDIO 2A: dual-deck, built-in 4-ch interface (master + cue); no dedicated tap
  button; capture its MIDI map via learn mode (don't hardcode CC numbers).
- Autopilot override defaults to auto-resume after a configurable window (both modes
  built behind one state machine).

## Open decisions (decide before relevant code)

1. **Audio library (realtime playback):** BASS/ManagedBass (easy for DJ:
   decode/mix/tempo/ASIO/CoreAudio, but **commercial license required for distribution**) vs
   PortAudio/miniaudio (open, more work). Affects `docs/01` and `docs/11`. **Offline decode**
   (analysis only) is already resolved: WAV via a pure-managed decoder, other formats via the
   FFmpeg CLI (`Liveolator.Audio`, doc 17) — this decision is only about realtime playback.

## Docs to revise (still reflect the old Zalmanolator stack)

- `docs/01` audio source layer (NAudio/ASIO → audio lib / CoreAudio)
- `docs/05` controller mapping (DryWetMidi → RtMidi)
- `docs/08` visual engine (**replace**: projectM → texture/layer compositor)
- `docs/11` decks (output routing via cross-platform audio lib)

`docs/00-LIVEOLATOR-CONTEXT.md` is authoritative until these land.

## Working notes

- **Build status — read before building:** `docs/18-implementation-status.md` is the living
  map of what is already implemented in `Liveolator.Core` (Actions/doc 04, Mapping+MIDI
  I/O/doc 05, Beat clock primitives/doc 03). Check it first to avoid rebuilding existing seams.
- Core logic is pure C# (no UI, no native) so it unit-tests under xUnit without
  hardware — preserve that boundary.
- Existing Zalmanolator algorithms worth porting: FFT/spectrum (`AudioAnalyzer`), BPM
  logic (`BpmDetector`), effect math (`KaleidoscopeKernel`, echo/particles → GLSL).
