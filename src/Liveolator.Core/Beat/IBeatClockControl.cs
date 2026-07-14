namespace Liveolator.Core.Beat;

/// <summary>
/// The performer-driven control surface of a beat clock — tap, lock, half/double, nudge, and
/// downbeat reset. Separated from the read-only <see cref="IBeatClock"/> so action handlers depend
/// only on the operations they invoke, and any clock (manual now, audio-driven later) can offer the
/// same controls (doc 03/04).
/// </summary>
public interface IBeatClockControl
{
    /// <summary>True when tempo is frozen against re-estimation.</summary>
    bool IsLocked { get; }

    /// <summary>Records a tap at the given host time; repeated taps establish tempo and grid.</summary>
    void Tap(long hostTimeTicks);

    /// <summary>Freezes the current tempo.</summary>
    void Lock();

    /// <summary>Releases the tempo freeze.</summary>
    void Unlock();

    /// <summary>Halves the tempo, preserving the current musical position.</summary>
    void HalfTempo(long hostTimeTicks);

    /// <summary>Doubles the tempo, preserving the current musical position.</summary>
    void DoubleTempo(long hostTimeTicks);

    /// <summary>Shifts the grid by <paramref name="beatDelta"/> beats (positive = forward).</summary>
    void Nudge(double beatDelta, long hostTimeTicks);

    /// <summary>Re-anchors the grid so the given host time is bar 1, beat 1.</summary>
    void SetDownbeat(long hostTimeTicks);
}
