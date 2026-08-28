using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Studio.Set;

/// <summary>
/// Whether anything is driving the floor over a window of a track, measured bar by bar off the kick
/// strikes the analyzer already persisted (<see cref="Liveolator.Core.Analysis.Bpm.BpmResult.KickOnsetsSeconds"/>).
/// <para>The planner and the export gate both need one honest answer to that question, and this is the
/// cheapest place it can come from: no decode, no new catalog field, no analyzer bump. The measured
/// 2026-08-13 holes (10.5 s at -63.7 dB on join 1, 17.0 s on join 5) were kickless windows on both decks
/// at once, which is exactly what <see cref="LongestJointKicklessRun"/> counts.</para>
/// <para>Everything here answers in BARS on each track's own grid rather than in seconds, so a future
/// analyzer change moves the grid and the verdict with it.</para>
/// <para>Known ceiling: a kick-only break or a filtered kick still reads as covered — coverage sees strike
/// times, not level. Revisit only if ear-verification finds one slipping through.</para>
/// </summary>
public static class KickCoverage
{
    /// <summary>Least covered a mix-OUT window may be (owner decision, 2026-08-28). Leaving a record from a
    /// window that is not almost fully driven records that record's own hole into the mix.</summary>
    public const double MixOutFloor = 0.90;

    /// <summary>Least covered a mix-IN window may be — deliberately more lenient than
    /// <see cref="MixOutFloor"/>, because entering on a rising intro is normal practice.</summary>
    public const double MixInFloor = 0.75;

    /// <summary>Most consecutive bars with no kick on EITHER deck that a join may contain: two or more is a
    /// hole a listener hears (owner decision, 2026-08-28).</summary>
    public const int MaxJointKicklessBars = 1;

    /// <summary>
    /// The fraction of the <paramref name="bars"/> bars starting at <paramref name="startSeconds"/> that
    /// contain at least one kick strike. <c>null</c> when the answer is UNKNOWN — the kick onsets were never
    /// analyzed, or the track has no tempo to measure bars against. Unknown must never be read as "no
    /// kicks": every un-analyzed record would become unmixable.
    /// </summary>
    public static double? Fraction(MusicTrack track, double startSeconds, int bars)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bars);

        return CoveredBars(track, startSeconds, bars) is { } covered
            ? covered.Count(c => c) / (double)bars
            : null;
    }

    /// <summary>
    /// The longest run of consecutive bars over which NEITHER track has a kick — each side measured from its
    /// own window start, on its own bar grid. <c>null</c> when either side's kicks are unknown, since one
    /// unmeasured deck cannot prove the floor was empty.
    /// </summary>
    public static int? LongestJointKicklessRun(
        MusicTrack outgoing,
        double outgoingStartSeconds,
        MusicTrack incoming,
        double incomingStartSeconds,
        int bars)
    {
        ArgumentNullException.ThrowIfNull(outgoing);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bars);

        if (CoveredBars(outgoing, outgoingStartSeconds, bars) is not { } outgoingBars ||
            CoveredBars(incoming, incomingStartSeconds, bars) is not { } incomingBars)
            return null;

        int longest = 0;
        int run = 0;
        for (int bar = 0; bar < bars; bar++)
        {
            run = outgoingBars[bar] || incomingBars[bar] ? 0 : run + 1;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    // Bar-hit map for the window, or null when the track cannot answer. Built by mapping each strike onto a
    // bar index rather than scanning the strikes once per bar: the onset list runs to thousands of entries
    // on a long record, the window is at most 32 bars.
    private static bool[]? CoveredBars(MusicTrack track, double startSeconds, int bars)
    {
        IReadOnlyList<double> kicks = track.Bpm?.KickOnsetsSeconds ?? Array.Empty<double>();
        double barSeconds = SetTransitionPlanner.BarSeconds(track);
        if (kicks.Count == 0 || barSeconds <= 0.0)
            return null;

        var covered = new bool[bars];
        foreach (double kick in kicks)
        {
            int bar = (int)Math.Floor((kick - startSeconds) / barSeconds);
            if (bar >= 0 && bar < bars)
                covered[bar] = true;
        }

        return covered;
    }
}
