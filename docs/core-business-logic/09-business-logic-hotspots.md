# Business Logic Hotspots

- Last updated: 2026-08-01
- Scope analyzed: Rule concentration, cross-domain coordination, and likely change-risk areas.
- Confidence note: Medium to High; hotspot priority is an engineering assessment grounded in code structure.

## Highest-value hotspots

- `PerformanceActionDispatcher` plus action handlers: architectural seam with broad blast radius. Preserve unique ownership, feedback semantics, logging, and serialization compatibility.
- `DeckActionHandler` and synchronization helpers: many commands, slot addressing, feedback, tempo/phase/loop/cue calculations, and native engine coordination converge here.
- `MusicLibrary`, `TrackAnalyzer`, and catalog reanalysis: expensive partial-failure workflows combine filesystem identity, decoding, metadata, analysis provenance, and persistence.
- `LivePlaylist`, `PlaylistActionHandler`, `DeckTrackLoader`, and `PlaylistAudioPlayer`: queue invariants span Core and Audio orchestration; concurrency and deck-slot routing deserve focused tests.
- `AutopilotEngine`: trigger/condition/cooldown/override/randomness state is compact but rule-dense and show-critical.
- `StudioArranger`, `StudioTransport`, `TempoCurve`, and render planning: boundary-crossing timing math can create audible errors even when state is valid.
- `ExtensionPackageValidator` and `ExtensionInstaller`: security-sensitive archive, path, signature, dependency, and atomic-install logic.
- `ServiceConfig`: large composition hotspot. It should remain wiring-only; any business decision added there becomes difficult to test and reuse.

## Coupling observations

Some product behavior resides in App view models because it depends on presentation workflow. Played-history reconstruction and startup restoration are examples. These should be documented and tested as orchestration rules or moved behind Core policies if reused by MCP or automation.

## Consolidation status

- Completed: queue-advance history detection moved from `DjViewModel` into the pure Playlist-domain `PlayedHistoryTracker`. The App now only projects Core entries into display rows.
- Next safe candidate: evaluate `DjBrowserViewModel.FreeDeckSlot` as a reusable Core loading policy after comparing every deck-loading entry point.
- Keep separated: startup wiring, native engines, store implementations, and Avalonia projections. They depend on different lifecycle and failure semantics and are not duplicate Core logic.

## Code References

- `src/Liveolator.Core/Audio/DeckActionHandler.cs`
- `src/Liveolator.Core/Library/Music/MusicLibrary.cs`
- `src/Liveolator.Core/Autopilot/AutopilotEngine.cs`
- `src/Liveolator.Core/Playlist/PlayedHistoryTracker.cs`
- `src/Liveolator.Core/Studio/StudioArranger.cs`
- `src/Liveolator.Media/Extensions/ExtensionInstaller.cs`
- `src/Liveolator.App/Composition/ServiceConfig.cs` — `ServiceConfig`
