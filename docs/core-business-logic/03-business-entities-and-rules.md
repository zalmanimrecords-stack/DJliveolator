# 03 — Business entities and rules

- **Purpose:** the entities the product depends on, and the invariants, validations and decision rules applied to them — with the point where each is enforced.
- **Scope:** `Liveolator.Core` and the policy that `Liveolator.Media` enforces on installation and persistence.
- **Source of truth:** `src/Liveolator.Core/**`, `src/Liveolator.Media/Extensions/**`, `tests/Liveolator.Core.Tests/**`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
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
  Only slots A (0) and B (1) take a crossfader factor; slots ≥ 2 — the hidden STUDIO decks — take
  unity, so their level is governed purely by channel gain driven from timeline automation.
  `MixerState.Channel` throws `ArgumentOutOfRangeException` outside `0..DeckCount-1`, which is `4` at
  this commit. *Enforced in* `MixerMath.DeckOutputGain`, `MixerState.Channel`. `Verified` — but see
  the in-flight reduction to two slots noted in [01](./01-system-overview.md).
- **The headphone cue is pre-fader.** `CueMixMath` deliberately ignores deck output gain so the cued
  track stays at a steady level wherever the crossfader sits, and blends cue against master with an
  equal-power curve.
- **EQ cut depth is a mixer-wide mode.** `EqCutMode` (EQ / DEEP / KILL) only changes how deep the cut
  half of each band goes; the boost half and band Q are fixed. Default is full kill.
- *Enforced in* `MixerState`, `MixerMath`, `CueMixMath`. `Verified`.

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
- **A manual beat grid survives reanalysis** unless overwrite is explicitly requested.
  `Needs validation` — the rule is stated in `docs/13-data-and-persistence.md` and the grid carries a
  manual flag, but the enforcement point was not re-proved in this pass. Item in [11](./11-open-questions-and-assumptions.md).
- **A per-file failure degrades to status, not an aborted scan.** *Enforced in* the library scan path;
  see [04](./04-critical-flows.md).

### Harmonic set building

`HarmonicSetBuilder` selects by Camelot compatibility, then by `HarmonicSetOptions`: `Length` counts
the seed, `BpmTolerance` (default 6.0 BPM) caps the per-step tempo change, and `Trend`
(`Any` / `Steady` / `Up` / `Down`) constrains direction. `Validate()` throws on nonsensical requests.
Ordering is deterministic so the same request yields the same set. `Verified`.

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
