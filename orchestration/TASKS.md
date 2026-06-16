# Orchestration Ledger

> One row per parallel task. The orchestrator maintains this. A task may start only when its
> **Files** do not overlap any other `in-progress` task's Files (and no `hot` file it needs is
> claimed). Update Status as it moves: `queued -> in-progress -> validating -> merged` (or
> `blocked`). See [`OWNERSHIP.md`](OWNERSHIP.md) for hot files and area ownership.

## Active

| Task | Owner | Branch | Files (exclusive) | Status | Depends on |
|------|-------|--------|-------------------|--------|------------|
| _example: keylock-native_ | _agent-1_ | _feat/keylock-native_ | _BassMixerBackend.cs, IBassMixerBackend.cs, TwoDeckBassEngine.Tempo.cs_ | _queued_ | _A1 hardware verify_ |

## Recently merged

| Task | Branch | Merged commit | Notes |
|------|--------|---------------|-------|
| vj-controls-ui (N3a / B1) | feat/studio-tab | f2f506c | view bindings only |
| keylock-core (N4 P1-2) | feat/studio-tab | 8783ab5 | Core action + engine state (swept into a parallel commit) |
| library-dedup (X5.1) | feat/studio-tab | daec181 | pure DuplicateFinder (commit also swept parallel Studio files) |

## Conventions

- **Branch name** = `feat/<task-slug>`; worktree path = `../Liveolator-wt/<task-slug>`.
- A task touching a **hot file** must be the only task doing so, or hand the hot-file edit to
  the orchestrator to apply on the integration branch first.
- A task is `validating` once the agent reports green; the orchestrator runs the merge gate.
