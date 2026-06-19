---
name: code-alignment
description: Align all of Liveolator's code — merge every feature branch and finished worktree into origin/master (resolving conflicts while preserving both sides), validate with a clean build + the full xUnit suite, push master, build the Windows installer, and prune the merged branches. Use when the owner says "align the code", "merge everything to main/master", "תיישר קו", "מזג הכל ל-GIT MAIN", or wants a release-ready master plus a fresh installer.
---

# Align the codebase onto origin/master

Bring every branch and finished worktree together on `master`, prove it green, push it,
and produce the Windows installer. The repo runs **many parallel agent/Codex worktrees**
(see the parallel-agent workflow), so the danger is never the merge mechanics — it is
clobbering an *active* session or pushing a half-done branch. Map first, never guess.

## Non-negotiable invariants

1. **Never merge or delete a branch that is checked out in a *locked* or active worktree.**
   `git worktree list` shows them; `[... ] locked` or a `.claude/worktrees/` /
   `.codex/worktrees/` path means a session is live. If its work is unmerged, **ask the
   owner** (merge its committed tip now / wait for it to finish / skip it) — do not decide
   for them. Untracked files in that worktree = WIP that will NOT come along; say so.
2. **Preserve both sides.** `master` usually has its own unique commits while a feature
   branch was open. Merge *into* master with `--no-ff` so nothing is lost, and resolve
   each conflict by intent (the incoming feature is usually the one being adopted), never
   by blindly taking one side of a whole file.
3. **Green before push.** A clean `dotnet build` **and** the full xUnit suite must pass on
   the merged `master` before `git push`. No exceptions.
4. **Verify the push against the real remote** with `git ls-remote`, not the local
   `origin/master` tracking ref (it can lie if a push reports "Everything up-to-date").
5. **Push only `master`.** Do not force-push, do not rewrite history, do not touch other
   sessions' branches on the remote.

## Procedure

### 1. Map the state before touching anything
```bash
git status && git branch --show-current
git fetch --all --prune
git branch -vv && git worktree list
git ls-remote --heads origin
```
For every candidate branch/worktree HEAD, classify it against master:
```bash
git log --oneline master..<ref>     # what it adds (empty ⇒ already merged)
git log --oneline <ref>..master      # what master has it lacks
git merge-base --is-ancestor <ref> master && echo "fully contained"
```
Decide the merge set: branches with commits *ahead* of master. Skip anything fully
contained. Flag active/locked worktrees for the owner (invariant 1).

### 2. Commit any real WIP on the current branch first
If the working tree has coherent, complete changes (e.g. a finished refactor), commit
them as focused commits **before** switching to master. Confirm dead-code removals leave
no dangling references: `git grep -n "<RemovedType>" -- 'src/**'`.

### 3. Merge each branch into master (smallest blast radius last → first is fine)
```bash
git checkout master
git merge <feature-branch> --no-ff -m "merge(<branch>): <what it brings>"
git diff --name-only --diff-filter=U      # list conflicts
```
Resolve conflicts by reading both sides and keeping the intended behavior; remove every
`<<<<<<< / ======= / >>>>>>>` marker; confirm referenced bindings/types still exist
(e.g. an .axaml `{Binding X}` needs `X` on the view-model). Then
`git add <files> && git commit --no-edit`. Re-check `git diff --diff-filter=U` is empty.

### 4. Build — clear the build locks first (this WILL bite you)
A running app or an orphaned test host holds the output DLLs and the build fails with
`MSB3021/MSB3027 ... being used by another process`. Kill them, then build:
```powershell
Get-Process -Name "Liveolator.App","Liveolator.Mcp" -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build Liveolator.sln -c Debug -v minimal
```
If a build error names a locking PID, `Stop-Process -Id <pid> -Force` and rebuild. Do not
kill unrelated `dotnet` hosts blindly — the lock messages name the exact culprit.

### 5. Test — and beware the false failure
```powershell
dotnet test Liveolator.sln --no-build -v minimal
```
A solution-wide run sometimes exits 1 from **parallel test-host file-lock contention**
(orphaned `testhost`/`.NET Host` processes), *not* a real failure — `Liveolator.App.Tests`
is the usual victim and prints no result line. Confirm by running it alone:
```powershell
dotnet test tests/Liveolator.App.Tests --no-build -v minimal
```
Only a genuine `Failed: N>0` blocks the push. Record the totals across all projects.

### 6. Push and verify against the real remote
```bash
git push origin master
git ls-remote --heads origin refs/heads/master   # must equal `git rev-parse master`
```

### 7. Build the Windows installer
```powershell
Get-Process -Name "Liveolator.App" -ErrorAction SilentlyContinue | Stop-Process -Force
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
```
`scripts/build-installer.ps1` publishes `Liveolator.App` self-contained (Release, win-x64),
verifies the BASS natives are present, and compiles `installer/windows/Liveolator.iss` with
Inno Setup 6 → `artifacts/dist/win-x64/LiveolatorSetup-<version>.exe`. The version is the
single `<Version>` in `src/Liveolator.App/Liveolator.App.csproj`. Confirm the produced exe's
`LastWriteTime` is fresh. (`build-installer.ps1` runs `fetch-bass.ps1` itself if natives are
missing; needs Inno Setup 6 — `winget install JRSoftware.InnoSetup`.)

### 8. Prune merged branches (optional cleanup — ask if unsure)
Only after master is pushed and green, and only for branches **fully contained** in master:
```bash
git worktree remove <path>            # remove a stale feature worktree BEFORE its branch
git branch -d <branch>                # -d (not -D) so git refuses if not merged
git push origin --delete <branch>     # remote branch (verify ancestor of master first)
git fetch --prune
```
**Leave** `master`, any active/locked worktree, and its branch alone.

## Done means
Build clean · full xUnit green (totals recorded) · `ls-remote` confirms `master` pushed ·
`LiveolatorSetup-<version>.exe` freshly built · merged branches pruned (or explicitly left).
Report the test totals, the remote SHA, and the installer path + size.
