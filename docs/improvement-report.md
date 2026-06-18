# Code Improvement Report
_Generated: 2026-06-18_

## Summary
Ran a continuous-improvement maintenance pass on `feat/studio-tab`. The baseline is green
(build clean, **2225 tests pass / 2 skip / 0 fail**). A full evidence-based discovery sweep of
the safe, non-contended scope (`Liveolator.Core`/`Audio`/`Visuals`/`Media`/`Midi`/`Online`/`Mcp`)
found **no `needs-refactor-now` candidates** — no oversized files, no dead code, no TODOs, no
weak tests, no stale code references. Per the loop's prime directive (evidence, not aesthetics),
**no changes were made and no commits were created.** The codebase is in good structural shape.

## Commits Made
None. Zero clear-evidence, low-risk `needs-refactor-now` candidates existed in the safe scope,
and manufacturing changes to hit a commit count would violate Safety Rule #1 (evidence, not
aesthetics) and risk racing the concurrent editor active on the App/Live tree.

## Validation Commands Run
- `dotnet build Liveolator.sln -c Debug` — **passed** (0 errors, 2 warnings)
- `dotnet test Liveolator.sln --no-build` — **passed** (Core 1091, App 535/2-skip, Audio 223,
  Media 142, Visuals 128, Midi 44, Integration 25, Online 23, Mcp 14 = **2225 pass / 2 skip**)
  - Note: a solution-wide run once reported exit code 1 from parallel test-host **file-lock
    contention** (orphaned `.NET Host`/`testhost` processes), not a real test failure — each
    project passes when run individually. Clearing stray processes resolves it.

## Improvement Candidates Found

### needs-refactor-now (addressed)
- None found in the safe scope.

### worth-refactoring-soon (deferred)
- **`tests/Liveolator.Core.Tests/Actions/PerformanceActionDispatcherTests.cs:171`**
  (`Constructor_StrictOwnership_AcceptsEveryKindExactlyOnce`) — the name promises an "exactly
  once" property but the body only asserts construction does not throw. Could add an explicit
  assertion that the built dispatcher routes to its handler. Deferred: needs intent confirmation
  and is near-aesthetic; sibling tests likely already cover duplicate/incomplete ownership.
- The 4 other assertion-free tests (`BassMixerTests.UnregisteredSlot_DropsCallWithoutThrowing`,
  `NextTrackPreloaderTests.FailingPreload_IsSwallowed`,
  `GlVisualPerformanceEngineTests.Deferred_operations_do_not_throw`,
  `PerformanceDeckSetTests.Dispose_IsIdempotent`) could assert a side effect rather than relying
  on "no exception." Deferred: each is a legitimate must-not-throw test; strengthening needs
  domain intent and risks over-specifying.

### acceptable-as-is
- **`src/Liveolator.Core/Actions/PerformanceActionKind.cs:26`** — "no projectM presets" is a
  deliberate design note, not a stale reference.
- **`src/Liveolator.Mcp/Session/FrktlPresetAuthoring.cs:45`** — "MilkDrop-style trails" describes
  a frame-feedback technique by analogy, not a removed dependency.
- All 5 assertion-free tests above — valid "must-not-throw" behavior tests (an unhandled
  exception fails the test in xUnit).

### unclear-needs-more-evidence
- None.

## Areas Intentionally Left Unchanged
- **App/Live tree (`feat/studio-tab` working copy).** Another actor has uncommitted in-flight
  work there: staged deletions of `MacroEncodersView.axaml(.cs)` / `MacroEncodersViewModel.cs` /
  `MacroEncodersViewModelTests.cs` and a `LiveViewModel.cs` edit. Excluded from candidates and
  untouched to avoid bundling their work or racing their edits.
- **`docs/01`, `docs/05`, `docs/08`, `docs/11`** — `CLAUDE.md` flags these as still reflecting the
  old Zalmanolator stack (NAudio / DryWetMidi / projectM). Revising design docs is judgment-heavy,
  owner-tracked work, not a mechanical maintenance change — left for the owner.

## Risks and Follow-up Items
- **Build/test environment:** orphaned `.NET Host` / `testhost` / MCP-host processes periodically
  lock output DLLs and cause `MSB3021`/`MSB3027` build failures and spurious solution-test exit-1.
  A pre-build "kill stray hosts" step (or `run.ps1` already does this for the app) would make CI
  and local validation deterministic. Worth a small `chore` once the tree is uncontended.
- **Concurrent editing on the shared tree** makes a commit-per-chunk loop unsafe here; the repo's
  own `scripts/new-agent-worktree.ps1` (worktree-per-agent) is the right venue for future runs.

## Suggested Improvements (next loop)
- Re-run this loop in an **isolated worktree** once the App/Live in-flight work is committed, so
  commits don't collide with the concurrent editor.
- A focused `tests` pass to add explicit side-effect assertions to the 5 must-not-throw tests,
  done interactively (each needs a one-line intent check with the owner).

## Topics for Treatment
- **Build determinism / test-host lifecycle** — eliminate the DLL-lock contention class.
- **Design-doc freshness** — the owner-tracked `docs/01/05/08/11` stack-migration debt.

## New Feature Ideas
- None surfaced by this structural pass. Runtime/behavior defects (the "many bugs" mentioned) are
  better served by the new `qa-engineer` agent + the QA report at
  `docs/qa-reports/qa-report-2026-06-18.md` (H1 already fixed) than by this maintenance loop.
</content>
</invoke>
