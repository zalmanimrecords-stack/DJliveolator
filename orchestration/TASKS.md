# Orchestration Ledger

> One row per parallel task. The orchestrator maintains this. A task may start only when its
> **Files** do not overlap any other `in-progress` task's Files (and no `hot` file it needs is
> claimed). Update Status as it moves: `queued -> in-progress -> validating -> merged` (or
> `blocked`). See [`OWNERSHIP.md`](OWNERSHIP.md) for hot files and area ownership.

## Active / queued

| Task | Lane | Branch | Status | Depends on |
|------|------|--------|--------|------------|
| P0 hot-file prep | (orchestrator) | feat/studio-tab | queued | — (append RecordToggle/KeyShift/Automix kinds + key-shift seam) |
| T6 N4.4 key-lock UI | App-feature | feat/keylock-ui | queued | T4 (done) |
| T3 X4 Push 1 + SysEx | Mapping/MIDI | feat/push1-profile | queued | T2 (done), T16 (done) |
| T8 X3 VJ authoring UI + video | Visuals+App | feat/vj-authoring | queued | T5 (done) |
| T9 X2 recording master | Audio | feat/recording | queued | T4 (done) + P0 |

> Wave-2 lesson: run subagents FOREGROUND (background agents do not survive parent-process exit
> — all 4 wave-1 background agents were killed mid-run; their work was salvaged from their worktrees
> and merged lane-by-lane through the gate).

## Recently merged

| Task | Merged commit | Notes |
|------|---------------|-------|
| vj-controls-ui (N3a / B1) | f2f506c | view bindings only |
| keylock-core (N4 P1-2) | 8783ab5 | Core action + engine state |
| library-dedup (X5.1) | daec181 | pure DuplicateFinder |
| T2 MIDI-learn fixes (X6) | 486bab9 | relative scaling + soft-takeover; Core 127 green |
| T1 missing-file relocate (X5.2 core) | 1527677 | RelocationPlanner + IFileExistenceProbe; Core 103 green |
| T5 GL strobe/transition (B2) | 0bfa8c3 | StrobeGate + quantized transition; Visuals 125 green |
| T4 key-lock native (N4 P3) | cf5063e | BASS_FX tempo path; Audio 82 green; **audible verify pending A1** |
| T16 any-MIDI-controller | 6284394 | generic profile + auto-pick (parallel agent, via worktree script) |

> Integration verified on the merged branch: Core 1032 / Audio 206 / Visuals 125, 0 failures.

## Conventions

- **Branch name** = `feat/<task-slug>`; worktree path = `../Liveolator-wt/<task-slug>`.
- A task touching a **hot file** must be the only task doing so, or hand the hot-file edit to
  the orchestrator to apply on the integration branch first.
- A task is `validating` once the agent reports green; the orchestrator runs the merge gate.
