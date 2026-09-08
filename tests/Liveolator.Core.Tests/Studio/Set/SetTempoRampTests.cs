using Liveolator.Core.Studio.Set;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// Choosing the tempo each pair of records meets at. One tempo for a whole set forces the records
/// furthest from it to carry the whole stretch; letting the tempo travel between joins spreads that cost
/// over the pairs that actually have to agree.
/// </summary>
public sealed class SetTempoRampTests
{
    [Fact]
    public void JoinTempos_MeetEachPairInTheMiddle()
    {
        // The midpoint is what minimises the LARGER of the two warps at a join, which is the one that is
        // audible: 134 and 142 meeting at 138 asks 3% of each instead of 6% of one.
        double[] joins = SetTempoRamp.JoinTempos(new[] { 134.0, 142.0, 138.0 });

        Assert.Equal(new[] { 138.0, 140.0 }, joins);
    }

    [Fact]
    public void JoinTempos_IsEmpty_ForASingleRecord()
        => Assert.Empty(SetTempoRamp.JoinTempos(new[] { 140.0 }));

    [Fact]
    public void WorstWarpPercent_TakesTheHarderOfARecordsTwoJoins()
    {
        // A record is stretched twice over its life — once to meet the record before it, once for the one
        // after — and the warp limit has to answer for the worse of them, not their average.
        double[] bpms = { 134.0, 140.0, 152.0 };
        double[] joins = SetTempoRamp.JoinTempos(bpms);

        // The middle record meets 134 at 137 (+2.24%) and 152 at 146 (+4.29%).
        double worst = SetTempoRamp.WorstWarpPercent(bpms, joins, index: 1);

        Assert.Equal(4.29, worst, 2);
    }

    [Fact]
    public void WorstWarpPercent_UsesTheOnlyJoin_AtEitherEnd()
    {
        double[] bpms = { 130.0, 140.0 };
        double[] joins = SetTempoRamp.JoinTempos(bpms);

        Assert.Equal(3.85, SetTempoRamp.WorstWarpPercent(bpms, joins, 0), 2);
        Assert.Equal(-3.57, SetTempoRamp.WorstWarpPercent(bpms, joins, 1), 2);
    }

    [Fact]
    public void WorstWarpPercent_IsZero_ForALoneRecord()
        => Assert.Equal(0.0, SetTempoRamp.WorstWarpPercent(new[] { 140.0 }, Array.Empty<double>(), 0));

    [Fact]
    public void JoinTempos_SpreadsTheStretchFurtherThanOneSetTempo()
    {
        // The reason the feature exists, as a property rather than an anecdote: no record is warped harder
        // than the worst record would have been under any single tempo for the same chain.
        double[] bpms = { 134.0, 136.0, 136.0, 142.0, 138.0, 140.0, 134.0, 134.0 };
        double[] joins = SetTempoRamp.JoinTempos(bpms);

        double rampedWorst = Enumerable.Range(0, bpms.Length)
            .Max(i => Math.Abs(SetTempoRamp.WorstWarpPercent(bpms, joins, i)));
        double flatWorst = bpms.Min(fixedTempo => bpms.Max(b => Math.Abs((fixedTempo - b) / b * 100.0)));

        Assert.True(
            rampedWorst < flatWorst,
            $"ramped worst {rampedWorst:F2}% should beat the best flat tempo's {flatWorst:F2}%");
    }
}
