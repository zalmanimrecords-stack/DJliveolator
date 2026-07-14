namespace Liveolator.Core.Audio;

/// <summary>
/// Pure conversion between the deck engine's normalized 0..1 hot-cue position fraction and the
/// sample offset that the persisted <see cref="Liveolator.Core.Persistence.TrackCueRecord"/> stores
/// (doc 11/13). The deck addresses cues as a fraction of the loaded stream; the on-disk store keeps
/// sample-accurate offsets paired with a sample rate, so this maps between the two using the track's
/// total length in seconds.
/// </summary>
/// <remarks>
/// Kept pure (no native, no IO) so the round-trip math unit-tests without BASS. A non-positive length
/// or sample rate makes the mapping undefined; callers get a clamped/zero result rather than a throw so
/// a degenerate stream never crashes a load/save (global standards #16/#26).
/// </remarks>
public static class HotCuePositionMapper
{
    /// <summary>
    /// Convert a 0..1 position fraction to a sample offset from the track start, given the track's
    /// total length in seconds and the sample rate the offset will be stored against. Returns 0 when
    /// the length or sample rate is non-positive (an unknown stream — store the cue at the start).
    /// </summary>
    public static long FractionToSamples(double fraction, double lengthSeconds, int sampleRate)
    {
        if (lengthSeconds <= 0.0 || sampleRate <= 0)
            return 0L;
        double clamped = Math.Clamp(fraction, 0.0, 1.0);
        return (long)Math.Round(clamped * lengthSeconds * sampleRate);
    }

    /// <summary>
    /// Convert a stored sample offset back to a 0..1 position fraction, given the track's total length
    /// in seconds and the sample rate the offset was measured against. Returns 0 when the length or
    /// sample rate is non-positive, and clamps the result to 0..1 (a hand-edited offset past the end
    /// recalls at the track end rather than seeking out of range).
    /// </summary>
    public static double SamplesToFraction(long samples, double lengthSeconds, int sampleRate)
    {
        if (lengthSeconds <= 0.0 || sampleRate <= 0 || samples <= 0)
            return 0.0;
        double totalSamples = lengthSeconds * sampleRate;
        return Math.Clamp(samples / totalSamples, 0.0, 1.0);
    }
}
