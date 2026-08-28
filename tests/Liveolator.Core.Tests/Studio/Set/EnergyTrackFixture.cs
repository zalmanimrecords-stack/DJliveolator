using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// Kick-onset arrays for the mix-point energy gate: a record whose floor is moving, and one with holes in
/// it at known bar positions. Everything is at 128 BPM, where a bar is exactly 1.875 s and a beat 0.46875 s
/// — both exactly representable, so the generated onsets carry no accumulated error.
/// </summary>
internal static class EnergyTrackFixture
{
    internal const double BarSeconds = 1.875;
    private const double BeatSeconds = BarSeconds / 4.0;

    /// <summary>
    /// A kick on every beat of <c>[startSeconds, endSeconds)</c>, minus every beat falling inside one of the
    /// <paramref name="holes"/> — a hole is how a breakdown reads to <see cref="Liveolator.Core.Studio.Set.KickCoverage"/>.
    /// </summary>
    internal static IReadOnlyList<double> Beats(
        double startSeconds,
        double endSeconds,
        params (double From, double To)[] holes)
    {
        var kicks = new List<double>();
        for (double t = startSeconds; t < endSeconds; t += BeatSeconds)
        {
            if (!holes.Any(h => t >= h.From && t < h.To))
                kicks.Add(t);
        }

        return kicks;
    }

    /// <summary>The <paramref name="bars"/> bars starting <paramref name="fromBar"/> bars after <paramref name="originSeconds"/>.</summary>
    internal static (double From, double To) Hole(double originSeconds, int fromBar, int bars)
        => (originSeconds + (fromBar * BarSeconds), originSeconds + ((fromBar + bars) * BarSeconds));

    internal static MusicTrack Track(string path, IReadOnlyList<double> kicks, double durationSeconds = 300.0)
        => SetTrackFixture.Track(path, durationSeconds: durationSeconds, kicks: kicks);
}
