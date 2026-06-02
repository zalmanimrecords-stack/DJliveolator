# 03 — Beat Engine

## Purpose

Replace the current simple bass-energy detector with a performance-grade beat clock
that yields stable BPM, confidence, beat/bar phase, and lock — usable to drive
visuals and quantize playlist actions.

## Existing code this replaces / evolves

- `MilkDropVisualizer.App/Audio/BpmDetector.cs` — energy-based: watches the bass
  band (first ~1/6 of the FFT), fires a beat when energy > 1.35× rolling average,
  keeps 12 recent intervals (0.2–2.0 s), BPM = from median. Hardcoded thresholds,
  no confidence, resets on track change.
- `MilkDropVisualizer.App/Helpers/BeatDetectorService.cs` — thread-safe static
  republisher of BPM/phase/beat flags consumed by overlays/mirrors.

The new engine **supersedes** `BpmDetector` and either replaces `BeatDetectorService`
with `BeatClockService` or makes the service a thin facade over the new clock so
existing overlay consumers keep working during migration (global standard #7, #20).

## Components (one responsibility each)

```text
AudioFrameData (doc 02)
   │ spectrum + mono PCM
   ▼
OnsetDetectionEngine   → multi-band spectral-flux onset envelope
   ▼
TempoEstimator         → tempo candidates over rolling 8–12 s window (autocorrelation
                         / comb filter), with half/double-time candidates
   ▼
BeatTracker            → locks beat phase to the onset envelope at the chosen tempo
   ▼
BeatGrid               → maps continuous time to beats/bars; downbeat anchor
   ▼
BeatClockService       → owns the loop, applies tap/lock/nudge, publishes state
TapTempoService        → converts tap timestamps into a tempo/phase correction
```

## Output state

```csharp
public sealed record BeatClockState(
    double Bpm,
    double Confidence,        // 0..1
    double BeatPhase,         // 0..1 within current beat
    double BarPhase,          // 0..1 within current bar
    int BeatCount,            // monotonic since grid reset
    int BarNumber,
    bool IsBeat,              // true on the frame a beat boundary is crossed
    bool IsDownbeat,
    bool IsLocked,
    BeatClockSource Source,   // Deck | System | Input | Manual | External
    IReadOnlyList<TempoCandidate> Candidates);

public sealed record TempoCandidate(double Bpm, double Strength);

public interface IBeatClock
{
    BeatClockState Current { get; }
    event EventHandler<BeatClockState>? StateChanged;  // at least per beat
}
```

`StateChanged` publishes by swapping an immutable record (no shared mutable state),
matching the doc 00 threading model.

## Required capabilities

- Multi-band spectral flux onset detection (not just bass energy).
- Tempo candidates over a rolling window; expose the full candidate list so the UI
  and the performer can pick when detection is ambiguous.
- Confidence score derived from candidate separation / phase stability.
- Half-time and double-time candidate handling (e.g. 70↔140, 87↔174).
- **Tempo lock** — freeze BPM to prevent jitter mid-performance; tracking continues
  to follow phase but will not re-estimate tempo while locked.
- **Tap tempo** correction (`TapTempoService`).
- **Phase nudge** forward/back (align grid to a track that drifted).
- **Reset grid / set downbeat** — establish bar 1, beat 1 at the current instant.
- Beat/bar **quantization** primitives consumed by visuals (doc 08) and playlist
  (doc 09): "run now / next beat / next bar / every N bars."

## Silence & low-volume handling

When onset energy falls below a floor, the engine drops confidence and refuses to
lock onto noise (avoids false BPM during silence — a stated success criterion).
Locked tempo is retained through brief silences (drops in a song) rather than reset.

## Manual & external clock sources

`BeatClockSource.Manual` lets the performer drive purely from tap/lock with no audio
analysis. `BeatClockSource.External` is the seam for a future `IExternalClock`
(Ableton Link / DJ-link, doc 01 future) feeding tempo/phase directly.

## Quantization helper

```csharp
public enum Quantize { Immediate, NextBeat, NextBar, EveryNBars }

public interface IBeatScheduler
{
    // Resolves to the time the action should fire given the current clock.
    void Schedule(Quantize when, int everyN, Action onFire);
}
```

This is the bridge used by `Visual.TransitionNextBar`, `Playlist.SkipOnNextBar`,
etc. (doc 04).

## Error handling & logging

- Analysis runs in try/catch; a bad frame logs context and is skipped, never
  crashing the clock loop.
- Tempo re-locks and large BPM jumps are logged (state transitions are useful for
  diagnosing a bad set), but per-frame state is not logged.

## Phase

Phase 2 (Beat Engine v1): onset/tempo/tracker/grid/clock + tap/lock/÷2/×2/nudge and
`BeatClockState`. Quantization helper used from Phase 3 onward.

## Risks

- BPM detection is inherently ambiguous; half/double must always be exposed to the
  performer rather than hidden (a core plan risk).
- Latency between true beat and `IsBeat` directly affects visual tightness — it is a
  measured metric (doc 14).
- The migration of existing overlay consumers from `BeatDetectorService` must keep
  current beat-reactive overlays working; covered by tests before old code is
  removed.
