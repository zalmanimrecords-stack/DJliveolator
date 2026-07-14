namespace Liveolator.Core.Playlist;

/// <summary>Desired tempo direction across a generated set.</summary>
public enum BpmTrend
{
    /// <summary>No tempo constraint — pick the most harmonically/temporally adjacent track.</summary>
    Any,

    /// <summary>Keep the tempo roughly level (each step within tolerance, either direction).</summary>
    Steady,

    /// <summary>Build energy — each track is the same tempo or faster, by at most the tolerance.</summary>
    Rising,

    /// <summary>Wind down — each track is the same tempo or slower, by at most the tolerance.</summary>
    Falling
}

/// <summary>
/// Shape of a harmonic set request. <see cref="Length"/> is the total number of tracks
/// including the seed; <see cref="BpmTolerance"/> caps the per-step tempo change (BPM).
/// </summary>
public sealed record HarmonicSetOptions(int Length, double BpmTolerance = 6.0, BpmTrend Trend = BpmTrend.Any)
{
    /// <summary>Validates the request, throwing for nonsensical values.</summary>
    public void Validate()
    {
        if (Length < 1)
            throw new ArgumentOutOfRangeException(nameof(Length), Length, "Set length must be at least 1.");
        if (BpmTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(BpmTolerance), BpmTolerance, "BPM tolerance cannot be negative.");
    }
}
