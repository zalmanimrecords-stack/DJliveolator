# Liveolator

A **cross-platform DJ + VJ performance application** (runs on Windows today; macOS is
the design target), controlled from hardware (Ableton Push 1 + Behringer CMD STUDIO 2A)
or any class-compliant MIDI controller, that plays and mixes music *and* drives a
real-time, beat-synced visual engine.

> Liveolator is the cross-platform successor to the Windows-only Zalmanolator. It keeps
> the platform-agnostic architecture designed for Zalmanolator's "Live Mode" and drops
> the Windows-locked stack (WPF, C++/CLI, NAudio) and projectM/MilkDrop.

## What it is

- **DJ engine:** two decks, software mixer (crossfader, per-channel EQ/filter), beat
  detection, low-latency audio, headphone cue.
- **VJ engine:** GPU-shader-based real-time manipulation of **images, video clips, and
  live camera/capture input**, composited in layers and synced to the beat
  (Resolume-style). **No MilkDrop/projectM.**
- **Hardware control:** Ableton Push 1 (visuals) + Behringer CMD STUDIO 2A (DJ
  transport + its built-in 4-channel audio interface).
- **One action layer:** hardware, UI, and automation all emit the same serializable
  `PerformanceAction`s; engines are driven only through a dispatcher.

## Platform & stack (proposed)

| Concern | Choice |
|---------|--------|
| Runtime / UI | .NET 8 + **Avalonia** (cross-platform XAML/MVVM) |
| Graphics / effects | **OpenGL via Silk.NET** (fragment shaders on textures) |
| Video decode | **FFmpeg** (frame → GL texture); libVLC as a faster-start alternative |
| Camera / capture | FFmpeg (dshow on Windows, avfoundation on Mac) or OpenCV |
| Audio (DJ) | **BASS / ManagedBass** (decided 2026-06-05; used under un4seen's free license — free while Liveolator is; see [`LICENSE`](LICENSE) / [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt)) |
| MIDI | **RtMidi / libremidi** (cross-platform) |

ASIO (Windows) and CoreAudio (Mac) are reached through the chosen audio library;
the app code only sees the platform-agnostic seam interfaces.

## Repository layout (planned)

```text
Liveolator/
  docs/            # architecture & design (inherited from Zalmanolator's Live design)
  src/
    Liveolator.Core/        # platform-agnostic: seams, beat engine, actions, mapping,
                            # playlist, autopilot, visual scene model  (no UI, no native)
    Liveolator.App/         # Avalonia UI
    Liveolator.Audio/       # audio I/O binding (BASS/ManagedBass) + offline decode
    Liveolator.Midi/        # RtMidi/libremidi binding
    Liveolator.Visuals/     # OpenGL/Silk.NET compositor + shader effects + FFmpeg decode
  native/          # native dependency setup scripts / binaries (gitignored)
  tests/           # xUnit tests for Core (pure logic)
```

## Status

Working integrated app (not a design sketch): a two-deck DJ engine (BASS/ManagedBass — **decided**,
not an open question), one-button SYNC with continuous phase-lock, a production-grade music library +
analysis, and a GL layer compositor with beat-reactive generators, all driven through one
`PerformanceAction` dispatcher and covered by ~1,000+ passing tests. The audio↔visual link runs off one
shared beat clock (the differentiator).

For the current state, verified bug map, and prioritized next steps see
[`docs/27-system-review-2026-06-10.md`](docs/27-system-review-2026-06-10.md) and
[`docs/22-status-and-roadmap.md`](docs/22-status-and-roadmap.md);
[`docs/00-LIVEOLATOR-CONTEXT.md`](docs/00-LIVEOLATOR-CONTEXT.md) holds the direction and what carries
over from Zalmanolator. Still open: keylock, the VJ authoring UI, and cross-platform packaging.

## License

Liveolator is free software, licensed under the **GNU General Public License,
version 3 or later (GPLv3+)** — see [`LICENSE`](LICENSE). It is and always will
be free of charge.

**Important — the BASS audio library is not GPL and not included here.** The
native BASS libraries (un4seen Developments Ltd.) are a separate, proprietary
dependency that this repository does *not* contain. They are fetched from
un4seen at build time (`scripts/fetch-bass.*`) and are used under un4seen's own
license. BASS is free only while the product using it is also free of charge and
generates no revenue; anyone who sells or otherwise monetizes Liveolator or a
derivative must obtain their own BASS license from <https://www.un4seen.com/>.
To keep this arrangement lawful under the GPL, `LICENSE` grants an additional
permission (GPLv3 §7) allowing Liveolator to be combined with BASS.

All bundled third-party components and their licenses are listed in
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).
