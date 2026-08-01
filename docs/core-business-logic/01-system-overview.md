# System Overview

- Last updated: 2026-08-01
- Scope analyzed: `src/`, representative tests, application and MCP entry points, and current implementation-status documentation.
- Confidence note: High for implemented domain boundaries; Medium for end-user availability that depends on native devices or runtime configuration.

## Purpose

Liveolator is a cross-platform DJ and VJ performance system. Its implemented business center is a shared, serializable action layer: UI, MIDI controllers, studio automation, and autopilot express intent as `PerformanceAction` values, and registered handlers drive audio, mixer, playlist, beat, recording, system-volume, and visual concerns.

The system supports four engine deck slots: two visible live-performance decks and two studio-oriented slots. It also manages a searchable music and visual library, offline analysis, imports from other DJ libraries, harmonic playlist construction, live queues, track-linked visual programs, authored visual scenes, managed add-ons, and an MCP interface for agent-assisted library and visual work.

## Actors visible in code

- Performer/operator: controls decks, mixer, visuals, queues, recording, and mappings through UI or MIDI.
- Studio arranger: places clips and automation on a project timeline and renders or plays the arrangement.
- Add-on author/operator: installs signed or developer-mode extension packages and authors visual/control presets.
- External AI client: invokes the MCP server's library, analysis, harmonic, playlist, enrichment, and visual tools.
- External metadata services: AcoustID and GetSongBPM-compatible adapters enrich catalog data when configured.

## Layer separation

- Core business logic: `Liveolator.Core` rules, immutable state, policies, clocks, actions, queues, analysis, and project models.
- Application orchestration: `Liveolator.App` composition, view models, startup restoration, and coordination.
- Infrastructure: `Liveolator.Audio`, `Media`, `Midi`, `Online`, `Platform`, and `Visuals` implement native I/O, files, networking, databases, and rendering.
- Presentation: Avalonia views and UI-specific view models under `Liveolator.App/Features`.

## Code References

- `src/Liveolator.Core/Actions/PerformanceActionDispatcher.cs` — `PerformanceActionDispatcher.Dispatch`
- `src/Liveolator.App/Composition/ServiceConfig.cs` — `ServiceConfig.Build` application composition root
- `src/Liveolator.Mcp/Program.cs` — MCP host entry point
- `src/Liveolator.Core/Studio/StudioProject.cs` — studio aggregate
- `src/Liveolator.Core/Library/Music/MusicLibrary.cs` — music-library domain entry point
