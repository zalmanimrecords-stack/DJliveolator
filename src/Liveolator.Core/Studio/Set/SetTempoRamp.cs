namespace Liveolator.Core.Studio.Set;

/// <summary>
/// Where the set tempo sits at each join when it is allowed to travel, instead of one tempo for the whole
/// set. Two records only have to agree while they are playing together; between joins nothing is being
/// beat-matched, so the tempo can walk to meet the next record.
/// <para>This is how a DJ works a night: match for the mix, then drift over the following record. It
/// spreads the stretch across the pairs that have to agree rather than making the records furthest from
/// one chosen tempo carry all of it — which is also what keeps a record near its own tempo, where the
/// time-stretch does least to it.</para>
/// <para>The ramp is confined to a record's SOLO stretch — a tempo that moved during a blend would have the
/// two decks running at different speeds, which is the one thing a beat-matched join cannot survive — and it
/// FILLS that stretch rather than hurrying into a fixed window, so the same move is spread over minutes and
/// no instant pulls the record hard (owner decision, 2026-08-28). <see cref="TempoIntegral"/> does the
/// geometry that follows.</para>
/// </summary>
public static class SetTempoRamp
{
    /// <summary>
    /// The tempo each consecutive pair meets at: their midpoint, which is the tempo that minimises the
    /// LARGER of the two warps — the audible one. Returns one tempo per join, so one fewer than the records.
    /// </summary>
    public static double[] JoinTempos(IReadOnlyList<double> sourceBpms)
    {
        ArgumentNullException.ThrowIfNull(sourceBpms);
        if (sourceBpms.Count < 2)
            return Array.Empty<double>();

        var joins = new double[sourceBpms.Count - 1];
        for (int i = 0; i < joins.Length; i++)
            joins[i] = (sourceBpms[i] + sourceBpms[i + 1]) / 2.0;
        return joins;
    }

    /// <summary>
    /// The signed warp the record at <paramref name="index"/> carries at whichever of its two joins asks
    /// more of it. A record is stretched once to meet the record before it and once for the one after, and
    /// the warp limit has to answer for the worse of the two; the ends have only one join, and a lone
    /// record has none.
    /// </summary>
    public static double WorstWarpPercent(IReadOnlyList<double> sourceBpms, IReadOnlyList<double> joinTempos, int index)
    {
        ArgumentNullException.ThrowIfNull(sourceBpms);
        ArgumentNullException.ThrowIfNull(joinTempos);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, sourceBpms.Count);

        double bpm = sourceBpms[index];
        if (bpm <= 0.0 || joinTempos.Count == 0)
            return 0.0;

        double worst = 0.0;
        if (index > 0)
            worst = Warp(joinTempos[index - 1], bpm);
        if (index < joinTempos.Count)
        {
            double outgoing = Warp(joinTempos[index], bpm);
            if (Math.Abs(outgoing) > Math.Abs(worst))
                worst = outgoing;
        }

        return worst;
    }

    private static double Warp(double tempo, double sourceBpm) => (tempo - sourceBpm) / sourceBpm * 100.0;
}
