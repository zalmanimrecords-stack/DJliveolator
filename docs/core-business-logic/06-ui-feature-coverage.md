# 06 — UI feature coverage

- **Purpose:** which implemented capabilities a user can actually reach, and which exist only in code.
- **Scope:** the Avalonia shell (`src/Liveolator.App`), the built-in controller profiles, and the MCP tool surface.
- **Source of truth:** `src/Liveolator.App/Shell/MainWindowViewModel.cs`, `src/Liveolator.App/Features/**`, `src/Liveolator.Core/Mapping/Profiles/**`, `src/Liveolator.Core/Actions/PerformanceActionKind.cs`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High for the shell surfaces and the action-kind reachability analysis; Medium for anything requiring a device to become visible.
- **Related:** [flows](./04-critical-flows.md) · [domains](./02-core-domains.md) · [improvements](./14-final-improvement-report.md)

## How reachability was determined

Three routes can carry an intent into an engine, and a capability is reachable if any one of them
covers it:

1. **On-screen control** — a view model constructs a `PerformanceAction` and dispatches it. 55 of the
   76 declared action kinds are referenced somewhere in `Liveolator.App`.
2. **A built-in controller profile** — `Push1Profile`, `CmdStudio2AProfile` or `DdjFlx4Profile` ships
   a binding. `GenericControllerProfile` ships none.
3. **A learned or imported mapping** — `MappingsViewModel.BuildTargets` offers a **fixed list of 28
   learn targets** plus one per controllable generator-preset parameter; the global learn coordinator
   (`GlobalMidiLearnCoordinator.TryCaptureUiAction`) can only capture an action an on-screen control
   already emits. Anything outside both lists is bindable only by hand-writing or importing a mapping
   JSON file.

An action kind with no on-screen emitter, no built-in binding and no learn target is therefore not
reachable by a user through supported means.

## Shell surfaces

Seven tabs: LIVE, DJ PRO, STUDIO, VJ, LIBRARIES, ADDONS, SETTINGS. MIDI mapping is embedded in the
SETTINGS tab (`SettingsView.axaml` hosts `MappingsView`), not a tab of its own. Modal windows:
terms of use, track editor, playlist builder, folder status, update available, confirmation.

## Coverage matrix

| Feature | Domain | Implementation | Entry point | UI surface | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| Deck transport, cue, hot cues, loops, jog, key lock | Decks | `DeckActionHandler` | `Deck*` kinds | LIVE, DJ PRO deck views | `Full` | `DeckViewModel`, `DjProDeckView` |
| Crossfader, channel gain, EQ, filter, headphone cue | Mixer | `MixerActionHandler` | `Mixer*` kinds | LIVE, DJ PRO mixer views | `Full` | `MixerViewModel`, `DjProMixerView` |
| Master smart limiter (SMART/SAFE, character, ceiling) | Mixer | `MixerActionHandler` | `MixerLimiter*` | Shell top bar | `Full` | `MainWindowViewModel.Limiter` |
| Operating-system master volume | Platform | `SystemVolumeActionHandler` | `SystemMasterVolume` | Shell top bar | `Full` | `SystemVolumeControlViewModel` |
| Deck sync (toggle and once) | Beat | `DeckActionHandler` | `DeckSyncToggle`, `DeckSyncOnce` | Deck views; CMD STUDIO and FLX4 profiles | `Full` | mapping profiles |
| Tap tempo and beat nudge | Beat | `BeatActionHandler` | `BeatTapTempo`, `BeatNudge*` | LIVE beat controls; Push and CMD profiles | `Full` | `MappingsViewModel` targets |
| Beat lock, half/double tempo, reset grid, set downbeat | Beat | `BeatActionHandler` | `BeatLock`, `BeatUnlock`, `BeatHalfTempo`, `BeatDoubleTempo`, `BeatResetGrid`, `BeatSetDownbeat` | Push 1 only, and only lock/half/double | `Partial` | `Push1Profile`; no on-screen emitter, no learn target |
| Library scan, search, filter, badges, track editing | Library | `MusicLibrary`, `TrackAnalyzer` | LIBRARIES commands | LIBRARIES tab | `Full` | `LibrariesViewModel`, `TrackEditorWindow` |
| Import from other DJ applications | Library | Import readers in `Liveolator.Media` | LIBRARIES command and MCP `import_library` | LIBRARIES tab | `Full` | `LibrariesView.axaml.cs` |
| Online enrichment and BPM cross-check | Enrichment | `Liveolator.Online` | Track context action | LIBRARIES tab, SETTINGS credentials | `Full` | `TrackContextActions`, `TrackRowViewModel` |
| Playlist building (harmonic sets) | Playlist | `HarmonicSetBuilder` | Playlist builder | LIBRARIES → playlist builder window | `Full` | `PlaylistBuilderWindow` |
| Load or queue a track onto a deck | Playlist | `DeckTrackLoader` | `DeckLoadTrack`, `PlaylistAppendTrack` | LIBRARIES, DJ PRO browser | `Full` | `DjBrowserViewModel`, `LibrariesViewModel` |
| Live-queue editing: insert next, move, remove future | Playlist | `PlaylistActionHandler` | `PlaylistInsertTrackNext`, `PlaylistMoveTrack`, `PlaylistRemoveFutureTrack` | none for insert and move | `Internal only` | no emitter in `Liveolator.App`, no built-in binding, no learn target |
| Visual scene launching and macros | Visuals | `VisualActionHandler` | `VisualLoadScene`, `VisualSetMacro`, `VisualSetLaunchQuantize` | LIVE scene grid and visual control | `Full` | `SceneGridViewModel`, `VisualControlViewModel` |
| Visual blackout and strobe | Visuals | `VisualActionHandler` | `VisualBlackout`, `VisualToggleStrobe` | LIVE and DJ PRO | `Full` | `MasterFxViewModel` |
| Visual clip launch | Visuals | `VisualActionHandler` | `VisualLaunchClip` | none | `Internal only` | handler exists; nothing emits it |
| Visual scene and bank authoring | Visuals | `VisualBank`, `VisualScene`, `ILiveProfileStore` | file under `live/scenes/` | none | `Missing` | `ServiceConfig.LoadBanksOrStarter` reads banks; no application code saves one |
| Track-linked visual programme playback | Visuals | `ITrackVisualProgramStore` | LIVE visual control | LIVE tab | `Full` | `VisualControlViewModel` |
| Track-linked visual programme authoring | Visuals | `JsonTrackVisualProgramStore` | hand-written JSON | none | `Configuration only` | no save path in `Liveolator.App` or MCP |
| Visual media library (browse assets) | Visuals | `MusicLibrary` visual catalog | VJ tab | VJ tab | `Full` | `VisualLibraryViewModel` |
| Generator presets and controllable parameters | Visuals | `IGeneratorPresetRegistry` | `VisualLoadPreset`, `VisualSetMacro` | LIVE preset controls; learn targets generated per parameter | `Full` | `PresetControlsViewModel`, `MappingsViewModel` |
| Stem gain and mute | Decks | `DeckActionHandler` | `DeckStemGain`, `DeckStemMute` | DJ PRO stem rack | `Full` | `DeckStemRackViewModel` |
| Audio effect parameters | Audio effects | `AudioEffectActionHandler` | `AudioFxSetParameter` | DJ PRO FX rack | `Partial` | `DeckFxRackViewModel` |
| Audio effect load, unload, move, bypass, preset | Audio effects | `AudioEffectActionHandler` | `AudioFxLoad`, `AudioFxUnload`, `AudioFxMove`, `AudioFxToggleBypass`, `AudioFxLoadPreset` | none | `Internal only` | no emitter, no binding, no learn target |
| Master recording | Recording | `RecordingActionHandler` | `MasterRecordToggle` | LIVE master FX | `Full` | `MasterFxViewModel` |
| MIDI learn and mapping management | Mapping | `MidiControlSession` | learn, remove, import, export | SETTINGS → mapping panel | `Full` | `MappingsViewModel` |
| Studio timeline, automation, tempo curve, render | Studio | `StudioTransport`, `StudioArranger`, `MixPlan` | STUDIO commands | STUDIO tab | `Full` | `StudioViewModel` |
| Extension install, enable, trust | Extensions | `ExtensionInstaller`, `ExtensionPackageValidator` | ADDONS commands | ADDONS tab | `Full` | `AddonsViewModel` |
| UI themes and control skins | Presentation | `UiThemeManager`, `ControlSkinApplier` | SETTINGS | SETTINGS tab | `Full` | `BuiltInUiThemes`, `SettingsViewModel` |
| Update check and prompt | Update | `UpdateAvailabilityChecker` | startup | modal | `Full` | `StartupUpdateChecker` |
| Terms of use | Legal | `TermsOfUse` | first launch | modal, plus read-only in SETTINGS | `Full` | `App.axaml.cs` |
| Autopilot (unattended show rules) | Autopilot | `AutopilotEngine`, `AutopilotRuleSet` | none | none | `Missing` | no reference outside `Core/Autopilot` except persistence |
| Momentary EQ kill | Mixer | `MixerActionHandler` | `MixerEqKill` | none | `Internal only` | no emitter, no binding, no learn target |
| Cue-play preview (press and hold) | Decks | `DeckActionHandler` | `DeckCuePlay` | none | `Internal only` | as above |
| Loop halve and double | Decks | `DeckActionHandler` | `DeckLoopHalve`, `DeckLoopDouble` | none | `Internal only` | as above |
| Hot cue clear | Decks | `DeckActionHandler` | `DeckHotCueClear` | none | `Internal only` | as above |
| Quantize toggle | Decks | `DeckActionHandler` | `DeckQuantizeToggle` | none | `Internal only` | as above |
| Library Doctor health scan | Library | `LibraryHealthScanner`, `LibraryDoctor` | health-scan command | LIBRARIES → folders/status window | `Full` | `ScanHealthCommand`, `FoldersStatusWindow.axaml` |
| Library repair (apply a repair plan) | Library | `LibraryDoctor.Preview`, `LibraryRepairPlan`, `LibraryReferenceRewriter` | none | none | `Internal only` | no call site in `src`; the rewriter is registered in `ServiceConfig` but never resolved |
| Hidden STUDIO deck slots C and D | Decks, Studio | `MixerState.DeckCount` = 4, addressed by `TwoDeckBassEngine` and `MixPlan` | slots 2 and 3 | none | `Internal only` | `StudioViewModel` builds two lanes; nothing creates a clip on C or D — and an in-flight change removes the slots entirely ([01](./01-system-overview.md)) |
| MCP agent tools | Agent interface | `Liveolator.Mcp` | stdio | no in-app UI by design | `API only` | 22 attributed tools |

## Called out explicitly

**Implemented with no discoverable UI.** Autopilot is the largest: a complete, tested rule engine with
its own persistence format that nothing in the product ever runs. Visual scene and bank authoring is
second: banks are read at startup and can only be produced outside the product.

**Reachable only by editing configuration.** Track-linked visual programmes must be authored as JSON
by hand. Any action kind outside the 28 learn targets and the three built-in profiles requires a
hand-written mapping file to bind.

**Performance actions with no route at all.** Fourteen kinds have a handler but no emitter, no
built-in binding and no learn target: `MixerEqKill`, `DeckCuePlay`, `DeckLoopHalve`, `DeckLoopDouble`,
`DeckHotCueClear`, `DeckQuantizeToggle`, `AudioFxLoad`, `AudioFxUnload`, `AudioFxMove`,
`AudioFxToggleBypass`, `AudioFxLoadPreset`, `VisualLaunchClip`, `PlaylistInsertTrackNext` and
`PlaylistMoveTrack`. Several are documented in the enum as intended for hardware buttons, so the gap
is the mapping target list rather than the handler.

**Scaffolding that reads as a feature.** The library-repair types are the clearest case: a preview
type, a plan type and a reference rewriter all exist and none is ever called. Code shaped like a
safety mechanism, that never runs, is worse than no code — it invites the reader to assume repairs
are guarded.

**Effects rack is half-exposed.** Parameters can be moved from the DJ PRO FX rack, but an effect
cannot be loaded, removed, reordered or bypassed from anywhere.

**Possibly unused UI.** `DjView.axaml` and its code-behind are referenced by no other view and
`DjViewModel` is not one of the shell's tab pages — the DJ tab was replaced by DJ PRO.
`DjViewModel` itself is still very much alive: `MainWindowViewModel` takes it for the shared
`PerformanceDeckSet`, the mixer and the browser instance that DJ PRO reuses. So the view is dead
while the view model is load-bearing. `Needs validation` — confirm no launch path renders it before
treating the view as removable.

**False-positive risk.** This analysis is static. A binding created by an earlier session and saved
under `live/mappings/` would make an `Internal only` action reachable on that machine without
appearing here, and a control reachable only after a device connects cannot be observed without one.
