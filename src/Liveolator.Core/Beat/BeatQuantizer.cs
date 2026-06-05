namespace Liveolator.Core.Beat;

/// <summary>
/// Resolves the host time at which a quantized action should fire against a timeline. Pure math,
/// separated from the timer that an <see cref="IBeatScheduler"/> would use, so the snapping rules
/// are tested on their own (doc 03/04).
/// </summary>
public static class BeatQuantizer
{
    /// <summary>The default bar length, in beats (4/4).</summary>
    public const int DefaultBeatsPerBar = 4;

    /// <summary>
    /// Returns the host time at or after <paramref name="fromHostTimeTicks"/> when
    /// <paramref name="when"/> next occurs on <paramref name="timeline"/>.
    /// </summary>
    /// <param name="everyN">Number of bars for <see cref="Quantize.EveryNBars"/>; must be ≥ 1 there.</param>
    /// <param name="beatsPerBar">Beats per bar for bar-aligned quanta.</param>
    public static long ResolveFireTime(
        Quantize when,
        int everyN,
        long fromHostTimeTicks,
        IBeatTimeline timeline,
        int beatsPerBar = DefaultBeatsPerBar)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (beatsPerBar <= 0)
            throw new ArgumentOutOfRangeException(nameof(beatsPerBar), beatsPerBar, "Beats per bar must be positive.");

        return when switch
        {
            Quantize.Immediate => fromHostTimeTicks,
            Quantize.NextBeat => timeline.NextBoundary(fromHostTimeTicks, 1),
            Quantize.NextBar => timeline.NextBoundary(fromHostTimeTicks, beatsPerBar),
            Quantize.EveryNBars => EveryNBars(everyN, fromHostTimeTicks, timeline, beatsPerBar),
            _ => fromHostTimeTicks,
        };
    }

    private static long EveryNBars(int everyN, long fromHostTimeTicks, IBeatTimeline timeline, int beatsPerBar)
    {
        if (everyN < 1)
            throw new ArgumentOutOfRangeException(nameof(everyN), everyN, "EveryNBars requires at least one bar.");
        return timeline.NextBoundary(fromHostTimeTicks, (double)everyN * beatsPerBar);
    }
}
