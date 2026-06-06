---
name: add-beat-engine-feature
description: Add a capability to Liveolator's beat clock — a clock control (tap/lock/nudge/reset/half/double), a quantization primitive, a new BeatClockSource (manual/external/Ableton Link), or beat-reactive state — keeping the immutable BeatClockState and the one shared IBeatTimeline. Use when extending tempo detection, beat/bar phase, quantized scheduling, or the shared audio↔visual clock.
---

# Add a beat-engine feature

The beat clock is the spine of the product: **one shared clock drives both the DJ mix
and the visuals**, so beat-synced visuals and "control both at once" hold by
construction. Extend it without breaking that single-clock, immutable-state model.

Authoritative design: [`docs/03`](../../../docs/03-beat-engine.md).
Real types: `src/Liveolator.Core/Beat/` (`IBeatClock`, `IBeatTimeline`, `BeatClockState`,
`BeatClockSource`, `TempoCandidate`).

## Non-negotiable invariants

1. **Pure C# in `Liveolator.Core/Beat`** — no hardware, and nothing on the realtime path
   may block on analysis. The clock loop must survive a bad frame (log + skip, never
   crash — doc 03).
2. **State is published by swapping the whole immutable `BeatClockState` record** — never
   mutate shared state across threads (doc 00/03). `StateChanged` fires at least once per
   beat.
3. **One shared `IBeatTimeline`** — do not fork a second clock. Quantized launches resolve
   their fire time through `IBeatTimeline.NextBoundary(...)` / `IBeatScheduler`. A quantum
   is in beats (1 = beat, 4 = bar, 8/16 = phrase), and alignment composes.
4. **Expose ambiguity, never hide it** — half/double-time and competing tempos stay in the
   `Candidates` list for the performer to pick (a core plan risk).
5. **Confidence gating** — drop confidence and refuse to lock onto noise during silence;
   retain a locked tempo through brief drops rather than resetting (doc 03).

## Steps (TDD-first)

1. **Write the test first** under `tests/Liveolator.Core.Tests` (Beat). Beat logic is pure,
   so assert deterministically: `NextBoundary`/`PhaseAtTime` math against a known tempo;
   confidence drops to the floor on silence; `Candidates` includes the half/double
   hypothesis; tap/nudge/lock produce the expected state transition.

2. **Build the smallest component** in the doc-03 pipeline — each has one responsibility
   (`OnsetDetectionEngine` → `TempoEstimator` → `BeatTracker` → `BeatGrid` →
   `BeatClockService`, plus `TapTempoService`). Don't fold concerns together.

3. **A new clock control** (tap/lock/nudge/reset/÷2/×2) is reached as a `Beat*`
   `PerformanceAction` through the dispatcher's beat handler — use the
   `add-performance-action` skill to wire it. Beat-quantized kinds defer via
   `IBeatScheduler`; they never apply immediately.

4. **A new clock source** → add to the `BeatClockSource` enum and feed it through the
   **same** `IBeatTimeline` (`External` is the Ableton Link / DJ-link seam — when external,
   the timeline *is* Link's timeline).

5. **Log state transitions** (re-lock, large BPM jumps) for set diagnostics — but never log
   per-frame state.

## Validate

```powershell
dotnet build
dotnet test
```
Beat→`IsBeat` latency is a measured metric (doc 14) — keep an eye on it once wired to audio.
