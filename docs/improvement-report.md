# Code Improvement Report
_Generated: 2026-06-20_

## Summary
An autonomous maintenance pass over the Liveolator codebase. The code is in unusually
good shape — strict project standards (no dead code, 0 build warnings, ~332 test files,
no TODO/FIXME) mean the candidate pool was thin. Two safe, evidence-backed improvements
were made and committed. **The loop was then stopped early because a separate, active
session is concurrently modifying the main working tree** (see Risks).

## Commits Made
| # | Commit | Type | Scope |
|---|--------|------|-------|
| 1 | `4817e73` flag doc 20 as a superseded 2026-06-06 snapshot | docs | gap-analysis |
| 2 | `2f236ab` extract shared ControlBrush.Halo helper | refactor | controls |

## Validation Commands Run
- `dotnet build Liveolator.sln -c Debug` — passed (0 warnings, 0 errors) [baseline, from prior alignment]
- `dotnet test Liveolator.sln` — 2382 passed / 2 skipped [baseline, from prior alignment]
- `dotnet build src/Liveolator.App -c Debug` — passed (0 warnings, 0 errors) [after ControlBrush dedup]
- Control tests (`KnobSkinTests`, `JogKickEnergyTests`, `WaveformStripTests`) — passed

## Improvement Candidates Found

### needs-refactor-now (addressed)
- **`docs/20-dj-feature-gap-analysis.md:54`** — asserted "No `BeatGrid` type exists in code" /
  "grep: no `BeatGrid` in `src/`", but `BeatGrid.cs`, `OnsetPhaseLock.cs`, `MasterLimiter.cs`,
  `StructuralCueDetector.cs` all now exist → added a STALE banner pointing to the living
  status doc 18 (matches the repo's existing convention: doc 14, doc 24→27).
- **`Knob.cs` / `Fader.cs` / `Jog.cs`** — three byte-identical copies of a private
  `Halo(IBrush, double)` brush-opacity helper → extracted to `Controls/ControlBrush.cs`,
  call sites qualified, 3 copies removed. Pure function, behavior-preserving.

### worth-refactoring-soon (deferred)
- **`JsonHotCueStore.cs` / `JsonPlaylistStore.cs` (save path)** — duplicate the atomic
  temp-file-write-then-move pattern that `JsonFileSnapshotIo.SaveAsync<T>()` already
  provides, BUT with subtle variations (`.tmp` vs `.{Guid:N}.tmp`, `FileMode.Create` vs
  `CreateNew`). Consolidating would touch persistence atomicity/concurrency semantics —
  needs careful per-store verification + characterization tests first. **Not a pure no-op.**
- **Multiple `Json*Store` load paths** — the file-exists → deserialize → catch/warn → null
  block is repeated across ~6 stores; the core is already in `JsonFileSnapshotIo.LoadAsync<T>()`,
  but each store layers store-specific version checks on top. Low-priority; messages/return
  conventions (`null` vs `Array.Empty<T>()`) would need to standardize first.

### acceptable-as-is
- **`Fader.cs:75` / `Knob.cs:83` `CoerceUnit`** — 2-line NaN→0 unit coercer, below the
  10-line duplication threshold and idiomatic Avalonia property-coercer boilerplate.
- **`ArgumentNullException.ThrowIfNull` guards** (`MixerMath`, `CueMixMath`) — idiomatic
  C# 11 single-line guards; extraction would reduce, not improve, clarity.

### unclear-needs-more-evidence
- None. Dead-code discovery (cross-project public/internal boundaries, XAML bindings, MCP
  reflection surface, serialized DTOs) found **no provably-dead symbols**.

## Areas Intentionally Left Unchanged
- **Oversized files** (`ServiceConfig.cs` 1208, `LibrariesViewModel.cs` 894,
  `BassMixerBackend.cs` 849, `DeckViewModel.cs` 822, `StudioViewModel.cs` 744). Splitting
  the composition root or large view-models is high-blast-radius and explicitly outside the
  "smallest safe diff" mandate of this loop. Leave for a deliberate, planned refactor.
- **The other active session's files** — every modified/untracked file in the current
  working tree except my four control files belongs to a concurrent session (see Risks).
  Not staged, not touched, not reverted.

## Risks and Follow-up Items
- **⚠️ ACTIVE CONCURRENT SESSION on the main working tree.** During this loop, files I did
  not author appeared and kept changing (a source-vs-compiled-DLL line mismatch in
  `DeckSessionRestoreReproTests.cs` proves it was mid-edit). Two features are in flight:
  1. **Window-layout settings** — `App.axaml.cs`, `Shell/MainWindow.axaml.cs`,
     `Shell/MainWindowViewModel.cs`, `Core/Settings/AppSettings.cs`, new
     `Core/Settings/WindowLayoutSettings.cs`, `Media/JsonSettingsStore.cs` (+ tests).
  2. **Deck-session-restore fix** — `Composition/DeckSessionPersistence.cs` + new
     `Composition/DeckSessionRestoreReproTests.cs`.
  - The 2–3 App.Tests failures observed (`Repro_SecondStartupLoad_ClobbersRestoredBpmAndFirstBeat`,
    `Repro_UnreachablePath_IsSilentlyDropped_DeckComesBackEmpty`) are **that session's
    intentionally-failing repro tests** whose fix (`DeckSessionPersistence.cs`) is incomplete
    — NOT regressions from this loop. The `RescanAll` failure was parallel test-host contention
    (passes in isolation).
  - **Recommendation:** let that session finish and commit before any further alignment or
    cleanup. I left its work entirely intact.
- These two commits are **local only** (not pushed) — appropriate given the concurrent session.

## Suggested Improvements (next loop)
1. After the concurrent session lands, re-run the full suite to confirm those repro tests
   flip to green, then resume cleanup.
2. Consolidate the `Json*Store` save path onto `JsonFileSnapshotIo.SaveAsync<T>()` — but
   only with characterization tests pinning the atomic-rename + temp-naming behavior first.

## Topics for Treatment
- **Persistence-layer consistency** — standardize temp-file naming, `FileMode`, exception
  handling, and load-failure return conventions across the `Json*Store` family.
- **Large-file decomposition** — a planned, test-guarded split of `ServiceConfig.cs` and the
  900-line view-models, done as its own reviewed effort (not an opportunistic loop).

## New Feature Ideas
- (Out of scope for this loop; none surfaced that the existing docs/roadmap don't already cover.)
