# Code Improvement Report
_Generated: 2026-06-20 (loop 2)_

## Summary
An autonomous maintenance pass over the Liveolator codebase. The code remains in
unusually good shape — strict project standards mean a thin candidate pool: no
TODO/FIXME anywhere in `src`, no obsolete/legacy markers, all README/doc cross-links
resolve, and the only proven dead code was a single never-called method. Two safe,
evidence-backed changes were made and committed; everything riskier (audio/GL hot
loops) was deliberately deferred rather than gambled on. The loop began on a clean tree
after the owner committed an in-progress STUDIO feature (`9d735ad`), so no in-progress
work was swept up.

## Commits Made
| # | Commit | Type | Scope |
|---|--------|------|-------|
| 1 | `0a91715` remove unused `Cooldown.Validate()` | refactor | autopilot |
| 2 | `1a1bb2d` await writer task instead of blocking `Wait()` | test | audio |

## Validation Commands Run
- `dotnet restore Liveolator.sln` — passed
- `dotnet build Liveolator.sln --configuration Release --no-restore` — passed (1 warning, pre-existing)
- `dotnet test Liveolator.sln --configuration Release --no-build --no-restore` — passed (2,449 passed, 0 failed, 2 skipped)
- Per-commit: narrow project build + test (Core 1202 green; Audio 231 green)

Baseline before any change: build green, 2,446 passed / 0 failed / 2 skipped.
Warnings went 2 → 1 (the xUnit1031 blocking-task warning was resolved by commit 2).

## Improvement Candidates Found

### needs-refactor-now (addressed)
- **src/Liveolator.Core/Autopilot/Cooldown.cs:11-16** — `Cooldown.Validate()` had zero
  callers in the whole repo (full-repo grep; `AutopilotEngine` reads `rule.Cooldown` but
  never validates it, no test invokes it) → removed the dead method.
- **tests/Liveolator.Audio.Tests/Playback/StatefulBiquadTests.cs:33,62** — concurrency
  test used `Task.Wait()` (xUnit1031, deadlock risk) → made async and `await` the writer
  task; behavior preserved (still waits before asserting), warning gone.

### worth-refactoring-soon (deferred)
- **src/Liveolator.Audio/Render/OfflineMixRenderer.cs:160-164** — L/R channel filter
  cascade `filt(high(mid(low(x))))` is duplicated per channel. A `static ProcessCascade`
  helper would dedup it cleanly and is guarded by `OfflineMixRendererTests`, BUT it sits
  in a per-sample hot loop the module docs require to mirror the live mixer exactly →
  deferred; the ~2-line dedup doesn't justify adding indirection to perf-sensitive DSP
  without owner sign-off.
- **src/Liveolator.Audio/Render/OfflineMixRenderer.cs:138-148** — the 4-band biquad
  init + coefficient-set pattern repeats; could collapse to an array+loop. Same hot-loop /
  state-isolation caution as above → deferred.
- **src/Liveolator.Audio/Playback/BassMixerBackend.cs:790-828** — four near-identical
  `Ensure*Scratch` buffer-grow methods could become one `EnsureCapacity(ref float[], …)`
  helper (~30 lines saved). Realtime BASS backend; native lib absent in CI, so harder to
  validate safely → deferred (owner should confirm test coverage first).
- **src/Liveolator.Core/Dsp/MasterLimiter.cs:152-223** — 71-line per-frame `Process`
  loop with five interleaved DSP phases. Realtime DSP; medium risk → deferred.
- **src/Liveolator.Visuals/Gl/GlVisualPerformanceEngine.cs:336-464** — 128-line `Run`
  bundling window config + GL context + handler wiring. Cannot be unit-tested (GL) →
  deferred.

### acceptable-as-is
- **src/Liveolator.Visuals/Gl/LayeredQuadRenderer.cs:113-116 / 170-173** — a scan flagged
  the second frame-uniform set as "redundant," but that is an *assumption*; removing it
  would risk a real behavior change with no test to catch it. Left untouched.
- **tests/Liveolator.Audio.Tests/Playback/FakeLivePlaylist.cs:23** — `Changed` event
  (CS0067 "never used") is a required `ILivePlaylist` interface member; cannot be removed.
- **docs/01, 05, 08, 11** — still describe the old Zalmanolator stack (NAudio / DryWetMidi
  / projectM). Project `CLAUDE.md` keeps these as *intentional historical* references
  until the rewrite lands; not stale-by-accident.

### unclear-needs-more-evidence
- None this pass — the dead-code sweep surfaced exactly one proven item; everything else
  in DI/MCP-attribute/XAML-bound territory could not be ruled out as dynamically used and
  was left alone (the conservative default).

## Areas Intentionally Left Unchanged
- All UI ViewModels (`DeckViewModel`, `LibrariesViewModel`, `StudioViewModel`, etc.) —
  large but XAML/DI-wired; "dead-looking" members are often bound dynamically.
- `ServiceConfig.cs` (1,210 lines) — the DI composition root; splitting it is high-risk
  and not a smallest-diff win.
- Realtime audio (`BassMixerBackend`, `TwoDeckBassEngine`) and all GL/`Visuals` code —
  perf-sensitive and/or untestable without hardware in CI.

## Risks and Follow-up Items
- **Cooldown bars are now unvalidated anywhere.** `Cooldown(int Bars)` and
  `ScenePool.CooldownBars` accept negatives silently. Removing the dead `Validate()` did
  not change behavior (it was never called), but if validation is desired it should be
  wired into rule/profile construction — an owner/product decision, not a mechanical fix.

## Suggested Improvements (next loop)
- If the owner confirms `OfflineMixRendererTests` covers output bit-for-bit, do the
  `ProcessCascade` + filter-array dedup in `OfflineMixRenderer` as one reviewed commit.
- Audit `BassMixerBackend.Ensure*Scratch` consolidation behind a focused test first.
- Consider extracting window/GL-context setup out of `GlVisualPerformanceEngine.Run`
  once a smoke/integration harness exists for the compositor.

## Topics for Treatment
- Input validation policy for autopilot/scene config (cooldown bars, scene counts):
  validate-at-construction vs. clamp vs. leave-open — decide once, apply consistently.
- A consistent "grow-or-reallocate buffer" helper shared by the audio backends.

## New Feature Ideas
- None implied beyond the already-tracked roadmap gaps (VJ authoring UI, keylock,
  cross-platform packaging — see `docs/22-status-and-roadmap.md`).
