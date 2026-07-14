namespace Liveolator.Core.Beat;

/// <summary>
/// The continuous bijection between host (wall-clock) time and musical beat time at the current
/// tempo. Any consumer can ask "what beat/phase are we at, at time t?" and schedule precisely
/// against the same grid without waiting for an event — the basis for quantized launch and the
/// shared audio↔visual clock (doc 03, Ableton-Link-style).
/// </summary>
public interface IBeatTimeline
{
    /// <summary>Musical beat position at <paramref name="hostTimeTicks"/> (monotonic across the session).</summary>
    double BeatAtTime(long hostTimeTicks);

    /// <summary>Phase 0..1 within the alignment grid of <paramref name="quantumBeats"/> at the given time.</summary>
    double PhaseAtTime(long hostTimeTicks, double quantumBeats);

    /// <summary>Host time of the next <paramref name="quantumBeats"/> boundary at or after the given time.</summary>
    long NextBoundary(long fromHostTimeTicks, double quantumBeats);
}
