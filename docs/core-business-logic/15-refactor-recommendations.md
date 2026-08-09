# 15 — Refactor recommendations

- **Purpose:** proposed structural treatments for the hotspots. References [10](./10-business-logic-hotspots.md) rather than re-analysing it.
- **Scope:** code structure and testability. Product, security and delivery items are in [14](./14-final-improvement-report.md).
- **Last validated:** 2026-08-02 (against commit `6a32b80` plus the changes recorded below)
- **Confidence:** High for the items marked verified against the code; Medium for the remaining proposals.
- **Related:** [hotspots](./10-business-logic-hotspots.md) · [domains](./02-core-domains.md)

> **Execution note (2026-08-02).** This list was first written from size and coupling metrics. Each item
> has since been checked against the code it names. Three needed no change at all, one was already
> satisfied, and two produced work. Where a recommendation did not survive that check, the finding is
> recorded here rather than the original advice — a refactor list that recommends work already done is
> worse than no list.

## Done

### 1. Strengthen the queue invariant across the project boundary — **done**

*Addressed:* the two-assembly queue seam in [10](./10-business-logic-hotspots.md).
*Problem:* `LivePlaylist` (Core) owned the rule and `PlaylistAudioPlayer` (Audio) owned the advance
trigger. `PlaylistAudioPlayerTests` exercised the binding against a *fake* queue and `LivePlaylistTests`
exercised the queue with *no* engine — nothing proved the two assemblies agreed.
*Change:* added `tests/Liveolator.Audio.Tests/Playback/LiveQueueEngineInvariantTests.cs` — nine tests
driving the real `LivePlaylist` through the real `PlaylistAudioPlayer` over the existing fake engine:
single advance per end-of-track, end on another slot ignored, future edits never touching the deck,
`Now` protected from removal, quantised skip, running dry, auto-advance off, dispose, and an
end-plus-skip race. All pass — the invariant holds today and is now pinned.
*Deviation:* placed in `Liveolator.Audio.Tests` rather than `Liveolator.Integration.Tests`, because the
fake engine already lives there and `Integration.Tests` is reserved for real-media tests.

### 2. Extract the health-scan pipeline into Core — **done**

*Addressed:* `LibrariesViewModel`, the largest file in the repository.
*Problem:* the Library Doctor's hash/identity/scan pipeline — load known hashes, hash only duplicate
candidates, rebuild and persist identities, run the doctor — sat inline in a view model, so it could
only be tested through the headless UI harness.
*Change:* added `src/Liveolator.Core/Library/LibraryHealthScanner.cs` (pure orchestration over the
`IMediaIdentityStore`, `IFileContentHasher` and `LibraryDoctor` seams, with an injectable clock) and
`tests/Liveolator.Core.Tests/Library/LibraryHealthScannerTests.cs` (seven tests — the first this logic
has had that need no view model). `LibrariesViewModel.RunScanHealthAsync` now calls it and
`FillDuplicateHashesAsync` is gone from the view model. No behaviour change; the 93 Libraries app tests
pass unchanged. The file went 1964 → 1921 lines.

## Verified as needing no change

### 3. Deck-choice policy is not shared, by design

The original advice was to move `DjBrowserViewModel.FreeDeckSlot` into Core, gated on confirming that
every deck-loading entry point agrees on the rule. They do not, and the difference is deliberate:

| Entry point | Deck choice |
| --- | --- |
| `DjBrowserViewModel.LoadToFreeDeck` (double-click) | the not-playing deck when exactly one plays; otherwise nothing happens |
| `DjBrowserViewModel.LoadToDeck` / `StepAndLoad` | the explicitly chosen slot |
| `LibrariesViewModel.LoadSelectedToDeckA` (double-click) | always deck A |
| `LibrariesViewModel.PlaySelected` (audition) | always deck A, with `replacePlaying` |
| `TrackContextActions` | the explicitly chosen slot |

Unifying them would change what a double-click does in LIBRARIES, which is a product decision, not a
refactor. `FreeDeckSlot` is already `public static` and pure, so moving it to Core without unifying
callers would be churn. The second candidate — played-count updates in `LibrariesViewModel` — is
already correctly layered: `RecordPlayAsync` delegates to `MusicLibrary.MarkPlayed`, which owns the
rule, and only persists and reports. No state is duplicated.

### 4. `DeckActionHandler` is already routing-only

The advice was to push calculations out of its 28-arm switch. There are none left to push: jog and
bend maths live in `JogBendTracker` and `JogWheelSettings`, onset encoding in `DeckKickOnsetCodec`,
sync maths in `Audio/Sync`, and the switch arms are engine calls plus a feedback raise. The file is
584 lines because the action vocabulary is large, not because it mixes concerns. Splitting it would
also fight the dispatcher's single-ownership rule.

### 5. The destructive delete path already has its contract

The concern in [11](./11-open-questions-and-assumptions.md) was that a destructive library operation
might apply without a preview. What the code actually does:

- `VisualLibraryViewModel.DeleteAssetAsync` — the one path that deletes a user file — confirms first
  through `IConfirmationService`, and both delete commands are disabled unless a file remover *and* a
  confirmation service were injected. It is covered by `Delete_when_cancelled_keeps_everything`,
  `Delete_removes_file_from_disk_catalog_and_list` and
  `Delete_when_file_delete_fails_surfaces_status_and_keeps_catalog`.
- `LibrariesViewModel.RemoveSelectedIssueFromCatalogAsync` removes a catalog row for a file that is
  already missing from disk. It deletes nothing.

There is no unguarded apply step, because there is no apply step at all — see the dead-scaffolding
finding below.

## Remaining

### 6. Continue splitting `LibrariesViewModel` — revised

*Problem:* still 1921 lines after the extraction above.
*What the first attempt learned:* the obvious cut — lifting the Library Doctor UI concern into its own
view model — is a bad trade. That concern shares `IsScanning`/`IsAutoCueing` busy state, the
`ScanStatus` line, the `Folders` collection and `RefreshRows()` with the scan concern. A sub-view-model
would need roughly eleven constructor wires and bidirectional callbacks to move about 180 lines: more
coupling, not less. Shared *UI state* is what a tab legitimately is.
*Better boundary:* keep cutting along the same line as the health scanner — move **non-UI pipelines**
into Core services and leave the UI state where it is. Remaining candidates, each self-contained:
the import pipeline, the auto-cue pass, the rescan/re-analysis pass, and cue-presence refresh.
*Risk:* low per extraction, because none of them touch XAML bindings.
*Validation:* the 93 Libraries app tests, plus new Core tests per extracted service.

### 7. Extract sequenced coordinators from `ServiceConfig` — not attempted

*Problem:* 1468 lines interleaving registration with startup restoration, visual-bank loading and
engine construction, so an ordering bug is not independently testable.
*Proposed boundary:* extract only the independently testable sequences, starting with startup
restoration. Registration itself should stay flat.
*Not attempted* because the file is on the project's hot-file list and had uncommitted changes from a
concurrent session throughout this work.
*Validation it would need:* a headless startup test asserting restored state, run before and after.

### 8. Remove the unreached library-repair scaffolding

*Evidence:* `LibraryDoctor.Preview` and the `LibraryRepairPlan`/`LibraryRepairAction`/
`LibraryRepairPreview` types have **no call site anywhere in `src`**, and the
`LibraryReferenceRewriter` registered at `ServiceConfig.cs:529` is never resolved. The Doctor reports
issues; nothing applies a repair plan.
*Treatment:* either wire the repair flow up or delete the scaffolding — dead code that looks like a
safety mechanism is worse than absent code, because it reads as though repairs are guarded.
*Decide first:* whether library repair is a feature ([14](./14-final-improvement-report.md)).

## Deliberately not recommended

- **Do not collapse the domain namespaces.** The `PerformanceAction` contract and the persistence
  interfaces are seams, not duplication. Studio and autopilot should keep sharing only the dispatcher
  contract.
- **Do not move native BASS, RtMidi, OpenGL, FFmpeg, HTTP or filesystem implementations into Core.**
  The platform-neutral seam is what makes the business rules testable without hardware.
- **Do not merge UI title lookup or `SetEntryViewModel` construction into Core.** They are display
  projections and belong in presentation.
- **Do not refactor anything named in [11](./11-open-questions-and-assumptions.md) as unverified**
  until the question is answered. Refactoring behaviour nobody has confirmed is how a documented
  uncertainty becomes a silent regression.
