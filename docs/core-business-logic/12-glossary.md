# 12 — Glossary

- **Purpose:** the one definition of each recurring term. Terminology in every other document in this set matches this file.
- **Scope:** business and technical terms that appear across the documentation or the code.
- **Source of truth:** the types named in each row.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High.
- **Related:** [entities](./03-business-entities-and-rules.md) · [domains](./02-core-domains.md)

| Term | Meaning in Liveolator | Where it lives |
| --- | --- | --- |
| Performance action | One serializable command shared by hardware, UI, studio and automation | `PerformanceAction` |
| Action kind | The vocabulary entry that decides which handler owns a command | `PerformanceActionKind` |
| Dispatcher seam | The single routing boundary between intent and a concern handler | `PerformanceActionDispatcher` |
| Handler | The one type that owns a set of action kinds and drives an engine | `IPerformanceActionHandler` |
| Feedback | A handler's report of current state, used to light an LED or a button face | `ActionFeedbackState` |
| Beat clock | The source of BPM, beat and bar phase, confidence and lock state | `BeatClockState` |
| Timeline | The shared, Ableton-Link-style musical time every domain reads | `IBeatTimeline` |
| Quantisation | Deferring an operation to a musical beat or bar boundary | `Quantize`, `IBeatScheduler` |
| Deck | An independent playback slot. A = 0 and B = 1 are the live decks; C = 2 and D = 3 are hidden STUDIO slots the UI never populates | `MixerState`, `TwoDeckBassEngine` |
| Cue | A saved or computed position in a track. Distinct from the headphone cue bus | `HotCue`, `CueBusState` |
| Headphone cue (PFL) | Pre-fader listening: the deck sent to the headphones regardless of the crossfader | `CueMixMath` |
| Sync lock | Tempo, and optionally phase, alignment between a deck and the reference | `SyncLockState`, `SyncMode` |
| Pitch bend | A momentary rate offset that slides phase without moving the pitch fader | `DeckPitchBend` |
| Grid edit | Correcting the analysed base BPM the beat grid is drawn from, without changing audible pitch | `DeckSetGridBpm` |
| Downbeat | Beat one of the bar, as distinct from beat phase | `DeckSetDownbeat`, `DownbeatEstimator` |
| Key lock | Preserving musical pitch while tempo changes | `DeckKeyLockToggle` |
| EQ cut mode | How deep a channel's band cut is allowed to go: EQ, DEEP or KILL | `EqCutMode` |
| Smart limiter | The master brick wall in SAFE (fixed release) or SMART (programme-dependent release) mode | `LimiterSettings` |
| Live playlist | A per-deck editable Now / Next / Later performance queue | `LivePlaylist`, `TrackState` |
| Camelot key | The harmonic-mixing notation used to compare musical keys | `HarmonicSetBuilder` |
| BPM provenance | Where a track's effective tempo came from after merging local and online values | `BpmProvenance` |
| Soft takeover | Holding a target value until a physical control catches up to it | `SoftTakeover` |
| Relative encoding | How an endless encoder encodes direction: two's-complement, offset-binary or sign-magnitude | `RelativeEncoding` |
| Mapping profile | A named set of bindings for one device, exportable and importable | `ControllerMappingProfile` |
| MIDI learn | Capturing a physical control and binding it to a chosen target | `MidiLearnSession` |
| Scene | A named visual composition of layers, sources, blends and effects | `VisualScene` |
| Bank | A collection of launchable scenes | `VisualBank` |
| Macro | A normalised control mapped to one or more visual parameters | `VisualSetMacro` |
| Generator preset | A self-contained shader preset exposing up to five controllable parameters | `GeneratorPreset`, `.frktl` files |
| Track visual programme | A timed visual cue programme bound to one music track | `TrackVisualProgram` |
| Autopilot | The rule engine that emits ordinary performance actions without touching engines directly | `AutopilotEngine` |
| Override policy | What autopilot does after a manual gesture: auto-resume or pause until re-enabled | `OverrideMode` |
| Studio project | A timeline of deck clips, automation and tempo for playback or rendering | `StudioProject` |
| Mix plan | The resolved arrangement an offline render consumes | `MixPlan` |
| Add-on / extension | A validated installable package adding supported content or capability | `ExtensionPackage`, `.liveolator-pack` |
| Developer mode | The setting that relaxes publisher-trust checking for extensions | `ExtensionSettings` |
| Control skin | A filmstrip-image skin for knobs and faders | `ControlSkinFile` |
| MCP | The stdio server exposing music-intelligence and authoring tools to agents | `src/Liveolator.Mcp` |
| Seam | A Core interface whose implementation lives in an adapter project | `IAudioSource`, `IFileEnumerator`, and peers |
| Zalmanolator | The Windows-only predecessor this project replaces | `docs/00-LIVEOLATOR-CONTEXT.md` |
