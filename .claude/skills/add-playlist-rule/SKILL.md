---
name: add-playlist-rule
description: Add or change a playlist rule in Liveolator — a harmonic-set selection/ordering rule in HarmonicSetBuilder (Camelot compatibility, BPM trend/tolerance, deterministic tie-break), or a live Now/Next/Later queue behavior via ILivePlaylist (insert/reorder/safe-skip on beat-bar). Use when changing how the next track is picked, harmonic mixing logic, BPM trend, or live-queue editing.
---

# Add a playlist rule

Liveolator has **two distinct playlist surfaces** — pick the right one:

| Surface | What it is | Status |
|---------|-----------|--------|
| **Offline set generation** | `HarmonicSetBuilder` — picks/orders tracks by harmony + tempo | **Built**, pure C# |
| **Live queue** | `ILivePlaylist` — Now/Next/Later editing + safe skip during playback | **Designed, not built** (doc 09) |

Authoritative design: [`docs/09`](../../../docs/09-live-playlist-engine.md) (live queue)
and [`docs/03`](../../../docs/03-beat-engine.md) / [`docs/16`](../../../docs/16-track-analysis-library.md)
(harmonic rule). Real code: `src/Liveolator.Core/Playlist/`.

## A — Offline selection / ordering rule (HarmonicSetBuilder)

`HarmonicSetBuilder` greedily chains tracks: from the current track, pick the unused,
Camelot-compatible candidate that best fits the requested `BpmTrend` with the smallest
in-bounds tempo jump. **Pure and IO-free — it unit-tests without hardware.**

Rules to preserve:
- **Determinism** — same inputs must always yield the same set. The ranking is
  `tempo jump → harmonic affinity → title` (the last is the deterministic tie-break). Any
  new criterion must keep a total, stable order.
- **Never silently violate a constraint** — a missing-BPM track must not break a
  `Rising`/`Falling`/`Steady` request; it is allowed only under `BpmTrend.Any` (see
  `FitsTrend`). Mirror that guard for any new rule.
- **Reuse the harmonic law** — `Camelot.IsCompatible` (±1 same letter, or same number
  switched letter). Don't reimplement it.
- **Validate options** (`HarmonicSetOptions.Validate`) and **stop early** when no compatible
  track remains — return a shorter set, never pad with an incompatible track.

## B — Live-queue behavior (ILivePlaylist)

The live queue is a thin editing layer **over** the player, not a rewrite. Rules:
- **Editing `Upcoming` never touches `Now`** — playback continues uninterrupted (the
  central success criterion). `RemoveFuture` must refuse to remove `Now`.
- **Safe skip is beat-quantized** — `SkipOn(NextBar)` schedules the change through
  `IBeatScheduler` (doc 03) so transitions stay musical. `SkipNow` is immediate.
- **Tolerate stale input** — reorder/remove validate the id and ignore stale ids from a
  laggy UI without throwing; file-open/preload failures are logged with the track path and
  the bad track is skipped, never killing playback (global standards #16/#26).
- Live commands arrive as `Playlist*` `PerformanceAction`s through the dispatcher — wire a
  new one with the `add-performance-action` skill.

## Steps (TDD-first)

1. **Write the test first** in `tests/Liveolator.Core.Tests/Playlist/`
   (`HarmonicSetBuilderTests` is the model). Assert the new ordering deterministically and
   cover edge cases: missing BPM/key, no compatible candidate (early stop), trend bounds.
2. **Implement** in `HarmonicSetBuilder` (`PickNext`/`FitsTrend`/scoring) or
   `HarmonicSetOptions` — keep it pure. For live behavior, implement on the `ILivePlaylist`
   side and guard `Now`.
3. Keep both surfaces free of UI/IO concerns — Core stays platform-agnostic.

## Validate

```powershell
dotnet build
dotnet test
```
