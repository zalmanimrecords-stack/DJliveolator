# Liveolator — Multi-Agent Orchestration Protocol

> The source of truth for how more than one agent (Claude Code subagents, Codex, or humans)
> works on Liveolator **in parallel without corrupting each other's commits or builds**.

## Why this exists

When several agents share one working tree + git index, `git commit` commits the **entire
staged index** — not just the files an agent added. We observed this three times in one
session: one agent's work was swept into another's commit, builds went red from a third
agent's mid-edit, and two agents collided on `PerformanceActionKind.cs`. Discipline alone
cannot fix this. The fix is **isolation**: one agent, one git worktree, one branch, one
`bin/obj`.

## The model

```
Liveolator/                         integration tree — only the orchestrator merges here
../Liveolator-wt/<task-a>/          branch feat/<task-a>   (agent A: isolated tree+index+bin/obj)
../Liveolator-wt/<task-b>/          branch feat/<task-b>   (agent B)
../Liveolator-wt/<task-c>/          branch feat/<task-c>   (agent C)
```

`git worktree` gives each agent a fully separate working tree, index, and `bin/obj`, so:
- commits never sweep in another agent's files,
- builds never lock each other's output DLLs,
- a red mid-edit in one worktree is invisible to the others.

## The process (the orchestrator enforces all 7)

1. **Decompose by non-overlapping file ownership.** Every task declares the exact files it
   may edit. Two tasks that need the same file do **not** run in parallel — serialize them.
2. **Record in the ledger.** Add a row to [`TASKS.md`](TASKS.md): task, owner, branch, files,
   status, dependencies.
3. **Bootstrap a worktree** per task: `scripts/new-agent-worktree.ps1 -Task <name>`.
4. **Work in isolation.** The agent builds and tests only the projects it touched, inside its
   own worktree. **Never run the app from a build lane** (it locks `bin/Debug/*.dll`; see
   below).
5. **Validate before merge.** Clean build + the relevant tests green in the worktree, or it
   does not merge. No exceptions ("validate before finishing").
6. **Serialized merge gate.** Only the orchestrator merges, one branch at a time:
   `scripts/integrate-worktree.ps1 -Branch feat/<task>`. It rebases onto the integration
   branch, builds, tests, then fast-forward-merges. A red gate blocks the merge.
7. **Rebase-on-green.** After each merge, still-open worktrees rebase onto the updated
   integration branch so they integrate against current truth.

## Liveolator-specific rules (learned the hard way)

- **Hot files are owned, never shared.** Only the orchestrator edits these, or exactly one
  task at a time may. See [`OWNERSHIP.md`](OWNERSHIP.md). They are the enum/DI/theme/seam
  files that two agents collided on.
- **One run/verify lane.** Only one worktree may launch `Liveolator.App` at a time. Launching
  the app locks `bin/Debug/*.dll`, and any concurrent `dotnet build` then fails with
  MSB3027/MSB3021. Build/test lanes must NOT run the app. Close the app before an integration
  build.
- **PowerShell 5.1, ASCII-only scripts.** No smart quotes / em-dashes in `.ps1` (the repo's
  documented smart-quote trap).
- **Worktrees build clean and separate.** `git worktree` does not copy `bin/obj`; each builds
  its own, so no cross-lane lock.

## Integration branch

Default integration branch: **`feat/studio-tab`** (the current active line; `master` is not
yet fast-forwarded). Change the `-Base` argument of both scripts to retarget.

## Mechanism

- **Foundation (always on):** these scripts + the ledger protect the tree for any agent,
  including non-Claude ones (Codex, humans).
- **Large parallel bursts:** drive Claude Code's `Workflow` tool with `isolation: "worktree"`
  for deterministic fan-out + a coded merge phase over the same worktree model. Requires the
  owner's explicit opt-in (token cost).

## Quick start

```powershell
# orchestrator: open a lane for an agent
scripts/new-agent-worktree.ps1 -Task keylock-native

# agent: work inside ../Liveolator-wt/keylock-native, build+test there, commit to feat/keylock-native

# orchestrator: merge when the agent reports green
scripts/integrate-worktree.ps1 -Branch feat/keylock-native
```
