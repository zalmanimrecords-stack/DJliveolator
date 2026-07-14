namespace Liveolator.Core.Beat;

/// <summary>
/// Converts a series of tap timestamps into a tempo. Pure — timestamps are supplied by the caller,
/// nothing is read from a clock — so it is fully deterministic and unit-testable (doc 03). The
/// most recent tap also marks a beat boundary, usable as a phase anchor.
/// </summary>
public sealed class TapTempoService
{
    private readonly long _ticksPerSecond;
    private readonly int _maxTaps;
    private readonly long _resetGapTicks;
    private readonly List<long> _taps = new();

    /// <param name="ticksPerSecond">Resolution of the supplied timestamps; must be positive.</param>
    /// <param name="maxTaps">Size of the rolling window averaged for tempo; must be ≥ 2.</param>
    /// <param name="resetGapSeconds">A gap longer than this starts a fresh tap series.</param>
    public TapTempoService(long ticksPerSecond, int maxTaps = 8, double resetGapSeconds = 2.0)
    {
        if (ticksPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), ticksPerSecond, "Tick rate must be positive.");
        if (maxTaps < 2)
            throw new ArgumentOutOfRangeException(nameof(maxTaps), maxTaps, "Need at least two taps to derive a tempo.");
        if (resetGapSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(resetGapSeconds), resetGapSeconds, "Reset gap must be positive.");

        _ticksPerSecond = ticksPerSecond;
        _maxTaps = maxTaps;
        _resetGapTicks = (long)(resetGapSeconds * ticksPerSecond);
    }

    /// <summary>Number of taps currently in the window.</summary>
    public int TapCount => _taps.Count;

    /// <summary>True once enough taps exist to derive a tempo.</summary>
    public bool HasTempo => _taps.Count >= 2;

    /// <summary>Records a tap. Stale taps (after a long gap) restart the series; non-monotonic taps are ignored.</summary>
    public void Tap(long hostTimeTicks)
    {
        if (_taps.Count > 0)
        {
            long last = _taps[^1];
            if (hostTimeTicks - last > _resetGapTicks)
                _taps.Clear(); // too slow to be the same tempo — begin a new attempt
            else if (hostTimeTicks <= last)
                return; // out-of-order or duplicate timestamp
        }

        _taps.Add(hostTimeTicks);
        if (_taps.Count > _maxTaps)
            _taps.RemoveAt(0);
    }

    /// <summary>Clears the tap series.</summary>
    public void Reset() => _taps.Clear();

    /// <summary>
    /// Derives BPM from the average interval across the current window. Returns false until at
    /// least two taps exist.
    /// </summary>
    public bool TryGetBpm(out double bpm)
    {
        bpm = 0;
        if (_taps.Count < 2)
            return false;

        double averageIntervalTicks = (double)(_taps[^1] - _taps[0]) / (_taps.Count - 1);
        if (averageIntervalTicks <= 0)
            return false;

        double secondsPerBeat = averageIntervalTicks / _ticksPerSecond;
        bpm = 60.0 / secondsPerBeat;
        return true;
    }
}
