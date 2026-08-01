# Core Domains

- Last updated: 2026-08-01
- Scope analyzed: Core modules and their principal application, persistence, native, and MCP consumers.
- Confidence note: High; boundaries are explicit in namespaces, interfaces, handlers, and tests.

## Domain map

| Domain | Responsibility | Important dependencies |
| --- | --- | --- |
| Performance actions | Normalizes commands and routes each action kind to one owner | All controllable engines |
| Beat and synchronization | Tempo, phase, timeline, tap, quantization, scheduled launches | Audio frames, decks, visuals, playlist |
| Decks and mixer | Playback intent, cueing, loops, jog, sync, gain/EQ/filter/crossfade/cue bus | Audio adapter and beat domain |
| Library and analysis | Scan, classify, query, analyze, repair, relocate, and import media | Decoders, metadata readers, persistence |
| Playlist and harmonic planning | Live Now/Next/Later queues and compatible ordered sets | Catalog tracks, beat scheduler, deck loader |
| Visual performance | Banks, scenes, layers, macros, effects, generators, track-linked cues | Shared beat clock and GL/media adapters |
| Autopilot | Evaluates triggers, conditions, cooldowns, override policy, and emits actions | Dispatcher, beat/track context, scene pools |
| Studio | Timeline clips, automation, tempo curves, harmonic auto-arrangement, render plans | Dispatcher, host clock, audio renderer |
| Extensions and skins | Validates, trusts, installs, enables, and loads packaged capabilities | File/signature infrastructure and settings |
| Persistence/settings/update | Stores authored/live state and decides whether an update should be offered | Media and Online adapters |
| MCP interface | Exposes selected music-intelligence and authoring use cases | Core services and shared stores |

The action domain is the main cross-domain seam. Persistence interfaces remain in Core while concrete JSON/SQLite implementations live in Media. Native audio, MIDI, OpenGL, FFmpeg, filesystem, and HTTP details are adapters rather than business policy.

## Mixed concerns

`Liveolator.App` view models sometimes contain user-workflow policy, such as played-history presentation or load-versus-queue coordination. These behaviors are product-significant even though they are outside Core. `ServiceConfig` is intentionally orchestration-heavy and should not be treated as a domain service.

## Core consolidation map

| Area found outside its natural Core boundary | Target Core domain | Decision |
| --- | --- | --- |
| Sequential-advance versus set-reload detection in `DjViewModel` | Playlist | Merged into `PlayedHistoryTracker` on 2026-08-01 |
| Choosing a free live deck in `DjBrowserViewModel.FreeDeckSlot` | Audio/Playlist loading policy | Candidate: pure and reusable, but first verify all callers agree on the exactly-one-free-deck rule |
| Played-count updates initiated by `LibrariesViewModel` | Library | Candidate: `MusicLibrary.MarkPlayed` already owns the rule; centralize the event coordination rather than duplicate state |
| Startup restoration in `ServiceConfig` | Application orchestration | Keep outside Core; extract smaller coordinators only where sequencing is independently testable |
| UI title lookup and `SetEntryViewModel` construction | Presentation | Do not merge; these are display projections |
| Native BASS, RtMidi, OpenGL, FFmpeg, HTTP, and filesystem implementations | Infrastructure adapters | Do not merge into Core; preserve platform-neutral seams |
| Studio timeline actions and autopilot actions | Studio and Autopilot respectively | Do not merge the domains; both should continue sharing only the action dispatcher contract |

Consolidation therefore means moving reusable policy inward, not collapsing domain namespaces or binding projects. The shared `PerformanceAction` contract and persistence interfaces are deliberate seams rather than duplication.

## Code References

- `src/Liveolator.Core/Actions/PerformanceActionKind.cs`
- `src/Liveolator.Core/Beat/BeatTimeline.cs`
- `src/Liveolator.Core/Playlist/LivePlaylist.cs`
- `src/Liveolator.Core/Playlist/PlayedHistoryTracker.cs`
- `src/Liveolator.Core/Autopilot/AutopilotEngine.cs`
- `src/Liveolator.Core/Visuals/VisualActionHandler.cs`
- `src/Liveolator.Media/Extensions/ExtensionInstaller.cs`
