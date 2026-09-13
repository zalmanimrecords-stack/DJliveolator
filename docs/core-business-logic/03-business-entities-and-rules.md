# 03 — Business entities and rules

- **Purpose:** the entities the product depends on, and the invariants, validations and decision rules applied to them — with the point where each is enforced.
- **Scope:** `Liveolator.Core` and the policy that `Liveolator.Media` enforces on installation and persistence.
- **Source of truth:** `src/Liveolator.Core/**`, `src/Liveolator.Media/Extensions/**`, `tests/Liveolator.Core.Tests/**`.
- **Last validated:** 2026-09-11 (against commit `b809ec7`)
- **Confidence:** High for the rules cited with an enforcement point; anything else is labelled inline.
- **Related:** [domains](./02-core-domains.md) · [flows](./04-critical-flows.md) · [lifecycles](./08-state-machines-and-lifecycles.md) · [glossary](./12-glossary.md)

## Entities

| Entity | Business meaning | Important fields | Relationships and lifecycle |
| --- | --- | --- | --- |
| `PerformanceAction` | One serializable performer or automation intent | Kind, Value, Slot, Argument, InputMode, Origin | Routed to exactly one handler; the unit every input source produces |
| `BeatClockState` | Current tempo, phase and bar truth | BPM, beat and bar phase, confidence, lock | Produced by a manual, audio or deck-driven clock; consumed by every beat-reactive domain |
| `MixerState` | The whole software mixer, immutably | Crossfader, Curve, Channels, CueBus, CutMode, Limiter | Exactly two channels (A/B); replaced wholesale on every change |
| `DeckChannelState` | One channel strip | Gain, three-band EQ, filter, cue routing | Owned by `MixerState`; addressed by deck slot |
| `MusicTrack` | A catalogued playable and analysable file | Path, metadata, media kind, analysis status and provenance | Queried, queued, loaded onto a deck; lifecycle in [08](./08-state-machines-and-lifecycles.md) |
| `QueueEntry` | A track plus its position in a live queue | Path, Id, `TrackState` | Moves through Now and Upcoming; identified by a stable `Guid` |
| `HotCue` / `TrackCueSet` | Saved performance positions for a track | Slot index, position, colour, manual-or-auto provenance | Automatic structural cues merge with manual ones; manual wins |
| `VisualBank` / `VisualScene` / `VisualLayer` | The authored visual show hierarchy | Name, scenes, layers, sources, blends, effect chain | A bank holds scenes; a scene composites layers |
| `TrackVisualProgram` / `TrackVisualCue` | A timed visual programme bound to one track | Track path and fingerprint, timed cues, fallback | Resolves music time to visual source time |
| `AutopilotRuleSet` / `AutopilotRule` | Unattended-show policy | Trigger, condition, cooldown, action, optional scene pool | Persistable; no runtime host at this commit ([02](./02-core-domains.md)) |
| `StudioProject` / `StudioClip` / `AutomationLane` | A timeline arrangement | Name, Bpm, Clips, Automation, optional `TempoCurve` | Clips occupy deck lanes A/B; lanes produce parameter actions over project time |
| `DjSetPlan` / `SetBuildOptions` | A whole set arranged from the catalog, and how it was asked for | Tracks, transitions, tempo, warp ceiling, target loudness, rejections | Produced by `DjSetArranger`; realised as a `StudioProject` |
| `SetTransition` / `TransitionShape` / `TransitionAutomation` | One planned crossfade between two records | Mix-in and mix-out anchors, overlap bars, fader and EQ moves | Planned by `SetTransitionPlanner`; becomes automation on the timeline |
| `SetJoinAudit` / `KickCoverage` | Whether a join will actually work, judged before rendering | Phrase alignment, kick coverage, low-band behaviour | Computed from catalog analysis alone, without decoding audio |
| `RejectedCandidate` / `RejectReason` | Why a track never reached the timeline | Track, reason, needed warp | Reported per track so the caller can act rather than guess |
| `SongStructure` / `SongSection` | Where a track's intro, builds, drops and outro sit | Bar-snapped boundaries, section labels | Detected in-process by `NoveltyStructureDetector`; feeds cues and mix points |
| `ControllerMappingProfile` / `ControllerBinding` | The hardware-to-action contract | Name, DeviceHint, bindings (message, slot, mode, curve) | Captured by learn or imported; persisted per device |
| `ExtensionPackage` | An installable capability | Publisher, dependencies, content, hashes, enablement | Validated then installed then enabled or removed |

DTOs under `src/Liveolator.Mcp/Contracts` are projections of these entities, and the `*Snapshot`
records in `Liveolator.Media` are serialisation formats. Neither owns domain meaning.

## Rules

### Action routing

- **One owner per action kind.** `PerformanceActionDispatcher` builds its kind-to-handler map at
  construction and throws `ArgumentException` when two handlers claim the same kind. With
  `requireCompleteOwnership` it also throws when any kind is unowned. *Enforced in*
  `PerformanceActionDispatcher` constructor. `Verified`.
- **A failing handler never takes down the input pipeline.** Handler exceptions are logged with kind,
  slot and mode, then swallowed; an unknown kind is logged as a warning and ignored; a throwing
  `ActionDispatched` observer is logged and the action is still routed. *Enforced in*
  `PerformanceActionDispatcher.Dispatch`. `Verified`.

### Deck loading

- **A file is proved reachable before anything is dispatched.** An unreachable path returns
  `DeckLoadOutcome.FileMissing` with a message naming the file, and no action is emitted.
- **A playing deck is never cut off.** When the target deck reports `DeckPlayPause` active, the track
  is appended to that deck's queue as `PlaylistAppendTrack` and the outcome is `Queued`.
- **Except on an explicit audition.** `replacePlaying: true` — used by the library Play button —
  loads over the playing track deliberately.
- **A deep engine failure is not reported as success.** After dispatching `DeckLoadTrack` the loader
  re-reads the feedback seam; an unavailable state returns `LoadFailed` instead of `Loaded`.
- *All four enforced in* `DeckTrackLoader.Load`. `Verified`.

### Live queue

- **Now is protected from ordinary removal.** `RemoveFuture` ignores the id of the playing entry and
  logs why; an id that matches nothing upcoming is ignored the same way rather than throwing.
- **Editing the future never disturbs Now**; a skip is scheduled through the shared beat scheduler
  (`SkipOn`) rather than applied immediately.
- *Enforced in* `LivePlaylist`. `Verified`.

### Mixer and decks

- **Deck output gain is channel gain times crossfader gain.** The channel gain is clamped to 0..1.
  There are exactly two slots — A (0) and B (1) — and both take a crossfader factor.
  `MixerState.Channel` throws `ArgumentOutOfRangeException` outside `0..DeckCount-1`, where
  `MixerState.DeckCount` is `2`. *Enforced in* `MixerMath.DeckOutputGain`, `MixerState.Channel`.
  `Verified`. The hidden STUDIO slots C/D and their unity-crossfade branch were removed in `9734782`;
  see [01](./01-system-overview.md).
- **The headphone cue is pre-fader.** `CueMixMath` deliberately ignores deck output gain so the cued
  track stays at a steady level wherever the crossfader sits, and blends cue against master with an
  equal-power curve.
- **EQ cut depth is a mixer-wide mode.** `EqCutMode` (EQ / DEEP / KILL) only changes how deep the cut
  half of each band goes; the boost half and band Q are fixed. Default is full kill.
- *Enforced in* `MixerState`, `MixerMath`, `CueMixMath`. `Verified`.

### Beat-grid confidence and sync

- **Sync phase calculations use playback-time coordinates.** The engine divides source playhead
  positions and grid anchors by each deck's nominal playback rate and supplies effective BPM to
  `PhaseAlignmentCalculator`. Output latency is subtracted in playback seconds; returned snap
  offsets are multiplied by the follower rate before seeking in source seconds. This prevents
  false phase corrections when differently pitched tracks are already beatmatched. Regression
  coverage includes five-minute simulated mixes, master pitch, half-time followers, latency,
  one-shot alignment, bar alignment and continuous re-snaps (`BeatSyncMediaTimeTests`).
- **Phase sync is offered only against a grid both decks can vouch for.** `GridConfidence` carries
  `PhaseSyncReady`, `TempoTrusted` and `Analyzed` as three separate answers.
  `DeckSlot.PhaseSyncReady` defaults to **false** and resets to false on every load, so a track with
  no verdict — a pre-v12 catalog row, or a load through a path that supplies none — gets tempo match
  without a phase snap. Unknown means tempo-only.
- **The gate is two-sided.** The follower aligns onto an anchor built from the leader, so both the
  correction loop and phase alignment require *both* decks to be phase-sync ready; a downgrade logs
  which side closed it.
- **Tempo trust is separate from phase trust.** A smeared kick can leave the grid unusable for phase
  while the tempo remains sound, so a tempo downgrade is decided on `TempoTrusted`, never on
  `PhaseSyncReady`.
- *Enforced in* `Core/Analysis/Bpm/GridConfidence.cs`, `DeckActionHandler`
  (`PerformanceActionKind.DeckSetPhaseSyncReady`) and the deck slot state. `Verified` as logic;
  the audible result is a listening test ([11](./11-open-questions-and-assumptions.md)).

### Controller mapping

- **A knob never jumps its target.** `SoftTakeover` holds the target until the incoming hardware
  value crosses or meets it, then tracks directly. One instance per physical control. `Verified`.
- Velocity-zero NoteOn is normalised to NoteOff, absolute and relative encodings are converted per
  binding, and duplicate `(type, channel, data1)` bindings are reported by
  `MappingConflictDetector`. *Enforced in* `Core/Mapping`. `Verified`.

### Analysis and enrichment

- **Local values are never blindly replaced.** `MetadataMergePolicy` produces a `BpmProvenance` of
  `CrossChecked` when local and online agree within tolerance including half and double time,
  `Conflicted` when they disagree — keeping the local value and flagging it for review — and
  `LocalConfirmed` once the user confirms, which is never re-flagged. `OnlineFetched` is used only
  when local detection produced nothing. `Verified`.
- **A hand-corrected analysis survives reanalysis** unless overwrite is explicitly requested.
  `MusicTrack.AnalysisIsManual` is the flag; a re-tag or rescan of a manual track replaces only its
  `File` record and leaves BPM, grid, key and cues alone, and `CatalogReanalysisService` skips manual
  tracks unless forced. *Enforced in* `MusicLibrary` (the manual-track branch) and
  `CatalogReanalysisService`; covered by `CatalogReanalysisServiceTests`. `Verified` — this closes
  what was open question 9.
- **A failed analysis never replaces a good one.** Only a successful run overwrites stored analysis;
  a failure records status and leaves the previous values intact, and a hand edit no longer discards
  the kick positions measured from the audio. *Enforced in* `CatalogReanalysisService`. `Verified`.
- **An unreachable file is skipped, not failed.** A disconnected drive or an un-downloaded cloud
  placeholder is passed over by background analysis rather than marked failed and stripped of its
  BPM, key, cues and structure. `Verified`.
- **A per-file failure degrades to status, not an aborted scan.** *Enforced in* the library scan path;
  see [04](./04-critical-flows.md).

### Harmonic set building

`HarmonicSetBuilder` selects by Camelot compatibility, then by `HarmonicSetOptions`: `Length` counts
the seed, `BpmTolerance` (default 6.0 BPM) caps the per-step tempo change, and `Trend`
(`Any` / `Steady` / `Up` / `Down`) constrains direction. `Validate()` throws on nonsensical requests.
Ordering is deterministic so the same request yields the same set. `Verified`.

### DJ set building

The arranger turns a pool of catalogued tracks into a beat-matched `StudioProject`. Harmonic ordering
is delegated to `HarmonicSetBuilder` above; these rules cover tempo, transitions and gating.
*Enforced in* `Core/Studio/Set` (`SetBuildOptions`, `DjSetArranger`, `SetTransitionPlanner`,
`SetTempoRamp`). `Verified`.

- **Every mix point is phrase-quantized.** The meter is 4/4 and a phrase is 16 bars
  (`SetBuildOptions.BeatsPerBar`, `PhraseBars`). A requested overlap is rounded *down* to half a
  phrase and clamped to 8–32 bars: below 8 a blend reads as a mistake, above 32 two arrangements
  fight each other and residual grid error has a minute to become audible flam.
- **The warp ceiling is never suspended.** `MaxWarpPercent` (default 6, suited to 4/4 electronic
  material) caps time-stretch. Naming a `TempoBpm` does not exempt a track: one that cannot reach the
  chosen tempo inside the ceiling is rejected *and named*.
- **The set tempo is the DJ's decision, not a statistic.** With no `TempoBpm` the arranger takes the
  median of the tracks it chose — a default and nothing more, since a pool weighted toward one tempo
  pins it there.
- **A travelling tempo is exclusive with a fixed one.** `RampTempo` and `TempoBpm` are answers to the
  same question, so setting both throws rather than letting one silently win. When ramping, each
  consecutive pair meets at its **midpoint** — the tempo that minimises the larger, audible warp of
  the two.
- **Tempo only moves while a record is alone.** A ramp is confined to a record's solo stretch, because
  a tempo that moved mid-blend would run the two decks at different speeds, and it *fills* that
  stretch rather than hurrying, so no instant pulls a record hard.
- **A low-confidence grid is mixed short, or excluded.** A track failing the grid-confidence gate is
  never blended longer than the 8-bar floor; `ExcludeLowGridConfidence` keeps it out entirely instead.
- **Every track that misses the timeline is reported with the reason.** `RejectReason` distinguishes
  causes that imply different next moves — `NoHarmonicMatch` (widen the pool) versus `BlockedByTrend`
  (drop the trend or reseed low) versus `OutsideTempoRange` (widen the warp limit) versus
  `SeedOutsideTempoRange` (reseed). Members are appended, never reordered, because the names are the
  wire format the MCP contract reports.
- **A cap that was honoured is not a rejection.** Reaching the requested length reports
  `LengthCapReached` as an explicit non-rejection, so a capped build of a large catalog does not read
  as rejection-free.
- **Clips are level-matched before they are blended.** Every clip is gained toward `TargetLufs`
  (default −9, near the natural level of dance masters) so unequal masters sit level through each
  crossfade and the master limiter is barely working.

### Autopilot

A rule fires only when its trigger matches and its condition and cooldown both pass; scene choice is
restricted to the rule's curated pool and can be made deterministic with a seed; a rule that throws is
disabled for the remainder of the session rather than retried. *Enforced in* `AutopilotEngine`.
`Verified` as logic — but no host constructs it, so the rules do not run in the product
([02](./02-core-domains.md)).

### Extensions and authored files

- Installation validates package structure, contained paths, declared dependencies, hashes and
  signatures, and publisher trust before any content is activated; developer mode deliberately relaxes
  the trust posture. *Enforced in* `ExtensionPackageValidator` and `ExtensionInstaller`.
- Track visual programmes, `.frktl` presets and control skins are validated before use or
  persistence (`ControlSkinValidator` and the visual program store).
- Profile names are sanitised to a flat `<safe-name>.json` so a name cannot escape its folder
  (`LiveProfileStore`).

### Update offer

An update is offered only when the manifest parses, the installed version parses, the manifest
version is strictly greater, and it is not the exact version the user skipped. Every ambiguous case —
null manifest, unparsable version on either side — resolves to no offer. Leading `v` and a SemVer
pre-release or build suffix are tolerated by comparing the numeric core. *Enforced in*
`UpdateAvailabilityChecker.Evaluate`. `Verified`.

### Terms of use

The application will not present its shell until the current terms version is accepted; declining, or
a dialog that fails to show, closes the window through the normal teardown. A bump of
`TermsOfUse.CurrentVersion` re-triggers the gate. *Enforced in* `App.axaml.cs`
`EnforceTermsAcceptanceAsync` with `TermsOfUse` and `LegalSettings`. `Verified`.

## Rules with no runtime enforcement point

Native device latency, hardware LED feedback and real GL behaviour cannot be guaranteed by Core
rules; they are validated only by running the product on real hardware. Recorded in
[11](./11-open-questions-and-assumptions.md).
