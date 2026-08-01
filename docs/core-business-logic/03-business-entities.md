# Business Entities

- Last updated: 2026-08-01
- Scope analyzed: Domain records and state-bearing objects that affect user-visible behavior.
- Confidence note: High for fields and relationships; lifecycle persistence varies by configured store.

## Principal entities

| Entity | Business meaning | Relationships and lifecycle |
| --- | --- | --- |
| `PerformanceAction` | Serializable performer or automation intent | Has kind, value, slot, argument, mode, and origin; dispatched to one handler |
| `BeatClockState` | Current tempo/phase/bar truth | Produced by manual, audio, or deck-driven clocks and consumed across domains |
| `MixerState` / `DeckChannelState` | Four-deck mix configuration | Crossfader plus per-channel gain, EQ, filter, cue, and mute-related state |
| `MusicTrack` | Cataloged playable/analyzable media | Path, metadata, media kind, analysis/status; queried and placed in playlists |
| `QueueEntry` | Track plus live-queue state | Moves through Now, Upcoming, and played-related behavior |
| `HotCue` / `TrackCueSet` | Saved performance positions | Can combine automatic structural cues with manual provenance |
| `VisualBank`, `VisualScene`, `VisualLayer` | Authored visual show hierarchy | A bank contains scenes; scenes contain composited source/effect layers |
| `TrackVisualProgram` / `TrackVisualCue` | Timed visual program bound to a track | Resolves music time to visual source time with playback/fallback rules |
| `AutopilotRuleSet` / `AutopilotRule` | Unattended-show policy | Rules combine trigger, condition, cooldown, action, and optional scene pool |
| `StudioProject`, `StudioClip`, `AutomationLane` | Arrangement aggregate | Clips occupy deck lanes; lanes produce parameter actions over project time |
| `ControllerMappingProfile` / `ControllerBinding` | Hardware-to-action contract | Captured or edited mappings can be persisted and reloaded per device |
| `ExtensionPackage` contracts | Installable capability metadata | Publisher trust, dependencies, content, enablement, and installed state |

Technical DTOs under `Liveolator.Mcp/Contracts` are projections of these concepts, not separate business entities. Persistence snapshot records are serialization formats and should not be confused with domain ownership.

## Code References

- `src/Liveolator.Core/Actions/PerformanceAction.cs`
- `src/Liveolator.Core/Library/Music/MusicTrack.cs`
- `src/Liveolator.Core/Playlist/QueueEntry.cs`
- `src/Liveolator.Core/Visuals/VisualScene.cs`
- `src/Liveolator.Core/Visuals/TrackPrograms/TrackVisualProgram.cs`
- `src/Liveolator.Core/Studio/StudioProject.cs`
- `src/Liveolator.Core/Extensions/ExtensionContracts.cs`
