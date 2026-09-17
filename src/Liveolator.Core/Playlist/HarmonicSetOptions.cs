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
/// <param name="Genre">Whether the pool is gated to the seed's genre. <see cref="GenreMatch.Strict"/>
/// (the default) refuses only a candidate we can positively place in ANOTHER genre — an untagged track
/// is unknown, not wrong, and stays. A set pool has no ranking to demote into, so
/// <see cref="GenreMatch.Loose"/> and <see cref="GenreMatch.Off"/> both mean "do not gate" here; they
/// differ only on the suggestion surface, where there is an order to sink a mismatch to.</param>
public sealed record HarmonicSetOptions(
    int Length,
    double BpmTolerance = 6.0,
    BpmTrend Trend = BpmTrend.Any,
    GenreMatch Genre = GenreMatch.Strict)
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
