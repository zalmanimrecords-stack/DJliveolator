# 08 — State machines and lifecycles

- **Purpose:** the state a stateful entity or process can be in, and the only ways it moves between them. Every state list and transition table in this documentation set lives here.
- **Scope:** explicit enums, transition guards, immutable state replacement and event-driven lifecycles in Core and Media.
- **Source of truth:** `src/Liveolator.Core/**`, `src/Liveolator.Media/Extensions/**`.
- **Last validated:** 2026-08-01 (against commit `6a32b80`)
- **Confidence:** High where the states are an explicit enum; Medium where the lifecycle is coordinated in application composition.
- **Related:** [entities](./03-business-entities-and-rules.md) · [flows](./04-critical-flows.md)

## Deck synchronisation

`SyncLockState` (`src/Liveolator.Core/Audio/Sync/SyncLockState.cs`) is surfaced to the SYNC button and
the waveform through the action-feedback value, so the ordinal is part of the contract.

| From | To | Trigger | Guard |
| --- | --- | --- | --- |
| `Off` (0) | `Active` (1) | `DeckSyncToggle` or `DeckSyncOnce` | A tempo reference exists |
| `Active` | `Locked` (2) | Correction loop converges | Phase within lock tolerance |
| `Locked` | `Drifting` (3) | Phase slips past the re-snap threshold | — |
| `Drifting` | `Locked` | One-shot beat snap completes | — |
| `Active` or `Locked` | `OutOfRange` (4) | Tempo difference exceeds the stretch ceiling | No rate is applied; the deck holds its own tempo |
| any | `Off` | Sync disengaged | — |

`SyncMode` distinguishes tempo-only from phase-following behaviour. Exact timing is
adapter-sensitive: `Needs validation` on real hardware.

## Live queue entry

`TrackState` (`src/Liveolator.Core/Playlist/TrackState.cs`).

| From | To | Trigger | Guard |
| --- | --- | --- | --- |
| `Next` | `Now` | `Advance` or `SkipNow` | An upcoming entry exists |
| `Later` | `Next` | The entry ahead is promoted | — |
| `Now` | `Played` | End of track, or the next entry is pulled into Now | — |
| `Now` | `Now` (none) | Advance with an empty future | Now is cleared |
| `Next` or `Later` | removed | `RemoveFuture(id)` | The id is not Now and matches an upcoming entry |

`Now` is terminal with respect to ordinary removal: `RemoveFuture` refuses the playing entry rather
than throwing.

## Autopilot engine

States are the engine's running flag combined with its override policy.

| From | To | Trigger | Guard |
| --- | --- | --- | --- |
| Stopped | Running | `Start()` | — |
| Running | Suspended | `OnManualAction()` | — |
| Suspended | Running | Configured bar window elapses | `OverrideMode.AutoResume` |
| Suspended | Suspended | Bar window elapses | `OverrideMode.PauseUntilReenabled` — only an explicit re-enable resumes |
| Running or Suspended | Stopped | `Stop()` | Terminal until a new `Start()` |

A rule that throws is disabled for the remainder of the session; the engine keeps running. No host
constructs this engine at present ([06](./06-ui-feature-coverage.md)).

## Studio transport and clips

| From | To | Trigger | Guard |
| --- | --- | --- | --- |
| Stopped | Playing | Transport play | — |
| Playing | Stopped | Transport stop | Position is retained |
| Clip idle | Clip started | Project time crosses the clip start | Emitted by `StudioArranger` |
| Clip started | Clip stopped | Project time crosses the clip end | — |

Automation values are interpolated between keyframes rather than being states; tempo is resolved from
`TempoCurve` keyframes, falling back to the project's fixed BPM when the curve is empty.

## Extension installation

| From | To | Trigger | Guard |
| --- | --- | --- | --- |
| Candidate archive | Validated | `ExtensionPackageValidator` passes | Structure, paths, hashes, signature, dependencies |
| Candidate archive | Rejected | Any check fails | Terminal — content is never activated |
| Validated | Installed | `ExtensionInstaller` completes | Publisher trusted, or developer mode |
| Installed | Enabled | User enables | — |
| Enabled | Installed | User disables | — |
| Installed or Enabled | Uninstalled | User uninstalls | Terminal |

## Update decision

| From | To | Trigger | Guard |
| --- | --- | --- | --- |
| Unknown | None | Evaluation | Manifest null, or either version unparsable, or latest ≤ installed, or latest equals the skipped version |
| Unknown | Available | Evaluation | Manifest parses and is strictly newer and not skipped |
| Available | Skipped | User chooses Skip | The manifest version string is persisted verbatim |
| Available | Deferred | User dismisses | Re-evaluated at next startup |

## Track analysis status

`MediaAnalysisStatus` on a `MusicTrack` records the outcome of scanning and analysis per file, which
is what keeps a per-file failure from aborting a scan ([04](./04-critical-flows.md)). BPM provenance
is a separate axis (`BpmProvenance`, [03](./03-business-entities-and-rules.md)): `LocalConfirmed` is
terminal — once the user confirms a value it is never re-flagged as conflicted.

## Lifecycle inconsistencies found

- **A persistable state model with no runtime.** `AutopilotRuleSet` can be saved and loaded, and the
  engine's override machine is fully implemented, but nothing advances it in the product.
- **A view without a page.** `DjView` has no route into the shell while `DjViewModel` remains a
  live dependency of the shell — see [06](./06-ui-feature-coverage.md).
