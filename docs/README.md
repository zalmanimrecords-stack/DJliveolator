# Liveolator — Design Docs

Design specification for **Liveolator**, a cross-platform (Windows + Mac) DJ + VJ
performance application. These docs began as the "Live Mode" design for the
Windows-only Zalmanolator and are being adapted to the cross-platform direction.

## Read first

➡️ **[00 — Liveolator Context](00-LIVEOLATOR-CONTEXT.md)** — why this project exists,
the product definition, the reimagined visual engine, the cross-platform stack, what
carries over, and the open decisions. **This is the source of truth** where any
numbered doc still reflects the old Zalmanolator stack.

## Guiding principle (non-negotiable)

Hardware, UI, and automation never talk to the audio, beat, visual, or playlist
engines directly. Everything flows through one shared action layer:

```text
Hardware / UI / Autopilot
  -> Controller Mapping
  -> PerformanceAction
  -> Audio Engine / Beat Engine / Visual Engine / Playlist Engine
```

## Document index

| # | Document | Subsystem | Liveolator status |
|---|----------|-----------|-------------------|
| 00ctx | [Liveolator Context](00-LIVEOLATOR-CONTEXT.md) | Direction | **Authoritative** |
| 00 | [Architecture overview](00-architecture-overview.md) | Cross-cutting | Carries over |
| 01 | [Audio source layer](01-audio-source-layer.md) | Audio input | ⚠️ revise (audio lib / CoreAudio) |
| 02 | [Audio frame pipeline](02-audio-frame-pipeline.md) | Audio analysis | Carries over |
| 03 | [Beat engine](03-beat-engine.md) | Beat clock | Carries over |
| 04 | [Performance action system](04-performance-action-system.md) | Control core | Carries over |
| 05 | [Controller mapping engine](05-controller-mapping-engine.md) | MIDI input | ⚠️ revise (RtMidi) |
| 06 | [Push profile](06-push-profile.md) | Push 1 | Carries over |
| 07 | [DJ controller profile](07-dj-controller-profile.md) | CMD STUDIO 2A | Carries over |
| 08 | [Visual performance engine](08-visual-performance-engine.md) | Visuals | 🔁 **replace** (texture/layer compositor) |
| 09 | [Live playlist engine](09-live-playlist-engine.md) | Playlist | Carries over |
| 10 | [Autopilot show rules](10-autopilot-show-rules.md) | Automation | Carries over |
| 11 | [Deck A/B and DJ engine](11-deck-ab-pro-dj.md) | DJ playback | ⚠️ revise (output via audio lib) |
| — | [Sync behavior spec](SYNC-BEHAVIOR-SPEC.md) | DJ sync/beatmatch | **Proposed** — contract + acceptance tests |
| 12 | [UI modules](12-ui-modules.md) | UI | Carries over (WPF→Avalonia wording) |
| 13 | [Data and persistence](13-data-and-persistence.md) | Storage | Carries over |
| 14 | [Testing and validation](14-testing-and-validation.md) | Quality | Carries over |
| 15 | [Phased roadmap](15-phased-roadmap.md) | Delivery | Carries over (visual phases reframed) |
| 25 | [Track-linked media and VJ foundation](25-track-linked-media-and-vj-foundation.md) | Media / Visuals | Detailed implementation plan |

## Confirmed hardware targets

- **Visual controller:** Ableton **Push 1** (velocity-based color palette; User mode).
- **DJ controller:** Behringer **CMD STUDIO 2A** — dual-deck, class-compliant MIDI,
  built-in 4-channel interface (master + headphone cue). Both are class-compliant and
  work on Windows and Mac.

## Cross-platform stack (summary; full rationale in the context doc)

- .NET 8 + **Avalonia** (UI) · **OpenGL via Silk.NET** + GLSL (effects) · **FFmpeg**
  (video) · camera capture · **RtMidi/libremidi** (MIDI) · audio library **TBD**
  (BASS vs PortAudio/miniaudio).
