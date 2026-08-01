# Critical Flows

- Last updated: 2026-08-01
- Scope analyzed: Representative end-to-end paths traceable through Core, application orchestration, and adapters.
- Confidence note: High for sequencing; Medium for native side effects requiring runtime dependencies.

## Controller action to engine

1. `MidiInputPipeline` receives a library-neutral `MidiMessage`.
2. `MidiControllerRouter` diverts input to active learn mode or mapping.
3. `ControllerMapper` matches a binding and converts the hardware value.
4. `PerformanceActionDispatcher` routes the resulting action to its single concern handler.
5. The handler updates an engine and publishes feedback; `MidiFeedbackPublisher` and UI subscribers reflect it.

## Scan and analyze music

1. `MusicLibrary` enumerates configured folders and compares files through incremental-scan rules.
2. Metadata and audio decoders produce a `MusicTrack`; `TrackAnalyzer` derives tempo, beat grid, key, cues, and confidence where possible.
3. Per-file failures become status rather than aborting the entire scan.
4. The catalog store persists the resulting snapshot; query, facet, and sort policies project it for UI or MCP consumers.

## Load or queue a track

1. UI workflow selects a deck and asks `DeckTrackLoader` to handle a catalog track.
2. File reachability is checked.
3. An idle deck receives a load action; a playing deck receives `PlaylistAppendTrack`.
4. `PlaylistActionHandler` routes by deck slot. `PlaylistAudioPlayer` loads/plays Now and advances on the matching deck's end event.

## Autopilot tick

1. Host supplies `AutopilotTickContext` from beat and track state.
2. `AutopilotEngine` checks running/override state, triggers, conditions, and cooldowns.
3. Eligible rules emit the same `PerformanceAction` path used by humans.
4. Manual input invokes override policy; auto-resume or explicit re-enable controls recovery.

## Studio playback

1. `StudioTransport` advances project time from an injected host clock.
2. `StudioArranger` identifies clip start/stop events and interpolated automation.
3. Actions with studio origin drive deck/mixer handlers through the dispatcher.
4. Offline rendering instead builds a `MixPlan` consumed by the Audio renderer.

## Code References

- `src/Liveolator.Core/Mapping/MidiInputPipeline.cs`
- `src/Liveolator.Core/Library/Music/MusicLibrary.cs`
- `src/Liveolator.Core/Analysis/TrackAnalyzer.cs`
- `src/Liveolator.Core/Playlist/DeckTrackLoader.cs`
- `src/Liveolator.Audio/Playback/PlaylistAudioPlayer.cs`
- `src/Liveolator.Core/Studio/StudioTransport.cs`
