using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis.Bpm;

/// <summary>
/// The kick list the DJ PRO beat comb draws: only analysed strikes the kick-phase gate vouches for, with the
/// whole-frame quantisation of the analysis (11.61 ms frames at 44.1 kHz / hop 512, stored to the ms)
/// smoothed out against the neighbouring kicks' beat lattice.
/// </summary>
public sealed class FourOnTheFloorKicksTests
{
    private const double Bpm = 125.0;
    private const double Beat = 60.0 / Bpm;
    private const double Frame = 512.0 / 44100.0;

    // Measured over 2000 start offsets at 125 BPM: raw error up to 6.1 ms, smoothed at most 1.72 ms.
    private const double SmoothedToleranceSeconds = 0.002;

    [Fact]
    public void KicksAtTheFiltersEdge_AreStillOnTheLattice()
    {
        // The analysis keeps strikes up to 25 ms either side of the phase, so two kept kicks can sit 50 ms apart.
        double[] stored = Lattice(0.3137, 33).Select((t, k) => t + (k % 2 == 0 ? 0.025 : -0.025)).ToArray();

        Assert.Equal(stored.Length, FourOnTheFloorKicks.From(Vouched(stored)).Count);
    }

    [Fact]
    public void From_ReturnsNothing_WithoutOnBeatProof()
    {
        BpmResult vouched = Vouched(Lattice(0.3137, 32));

        Assert.Empty(FourOnTheFloorKicks.From(null));
        Assert.Empty(FourOnTheFloorKicks.From(vouched with { KickPhaseMarginRatio = null }));
        Assert.Empty(FourOnTheFloorKicks.From(vouched with { KickPhaseMarginRatio = 0.9 }));
        Assert.Empty(FourOnTheFloorKicks.From(vouched with { PhaseWindowDisagreementSeconds = null }));
        Assert.Empty(FourOnTheFloorKicks.From(vouched with { PhaseWindowDisagreementSeconds = 0.05 }));
        Assert.Empty(FourOnTheFloorKicks.From(vouched with { Bpm = 0.0 }));
        Assert.Empty(FourOnTheFloorKicks.From(vouched with { KickOnsetsSeconds = Array.Empty<double>() }));
    }

    [Theory]
    [InlineData(0.3137)]
    [InlineData(1.0021)]
    [InlineData(2.2489)]
    public void FrameQuantisedLattice_SmoothsBackOntoTheTrueKicks(double firstKickSeconds)
    {
        double[] truth = Lattice(firstKickSeconds, 64);
        double[] stored = truth.Select(Quantise).ToArray();

        IReadOnlyList<double> smoothed = FourOnTheFloorKicks.From(Vouched(stored));

        Assert.True(MaxError(stored, truth) > 0.004, "the fixture must carry real frame quantisation");
        Assert.True(MaxError(smoothed, truth) <= SmoothedToleranceSeconds,
            $"smoothed kicks must sit within 2 ms of truth; worst was {MaxError(smoothed, truth) * 1000:F2} ms");
        Assert.Equal(stored.Length, smoothed.Count);
        Assert.True(smoothed.Zip(smoothed.Skip(1)).All(p => p.First < p.Second), "order must be preserved");
    }

    [Fact]
    public void BreakdownGap_KeepsTheKicksEitherSideAccurate()
    {
        // 16 beats (~7.7 s) of breakdown with no kicks: the beat lattice carries the neighbours across it.
        double[] truth = Lattice(0.3137, 64).Where((_, k) => k is < 24 or >= 40).ToArray();
        double[] stored = truth.Select(Quantise).ToArray();

        IReadOnlyList<double> smoothed = FourOnTheFloorKicks.From(Vouched(stored));

        Assert.Equal(stored.Length, smoothed.Count);
        Assert.True(MaxError(smoothed, truth) <= SmoothedToleranceSeconds,
            $"worst was {MaxError(smoothed, truth) * 1000:F2} ms");
    }

    [Fact]
    public void AnOffLatticeKick_IsPulledBack_WithoutDraggingItsNeighbours()
    {
        double[] truth = Lattice(0.3137, 33);
        double[] stored = (double[])truth.Clone();
        stored[16] += 0.020; // a flam that passed the analysis' ±25 ms on-phase filter

        IReadOnlyList<double> smoothed = FourOnTheFloorKicks.From(Vouched(stored));

        Assert.True(MaxError(smoothed, truth) < 1e-9, $"worst was {MaxError(smoothed, truth) * 1000:F3} ms");
    }

    [Theory]
    [InlineData(Bpm / 2.0)]       // halved by a manual tempo edit: every other kick falls on the off-beat
    [InlineData(Bpm * 2.0 / 3.0)] // 3:2 edit
    public void KicksNotOnTheLatticeOfTheStoredTempo_ReturnNothing(double editedBpm)
    {
        // The edit keeps the list and its gate numbers, but they were measured at another tempo.
        BpmResult edited = Vouched(Lattice(0.3137, 64).Select(Quantise).ToArray()) with { Bpm = editedBpm };

        Assert.Empty(FourOnTheFloorKicks.From(edited));
    }

    [Fact]
    public void AListWithAnOffBeatPick_ReturnsNothing()
    {
        // Raw picks stored when the rounded margin passed the gate but the unrounded one did not.
        double[] stored = Lattice(0.3137, 64).Select(Quantise).Append(Quantise(0.3137 + (20.5 * Beat))).Order().ToArray();

        Assert.Empty(FourOnTheFloorKicks.From(Vouched(stored)));
    }

    [Fact]
    public void ALoneKick_IsReturnedAsIs()
    {
        Assert.Equal(new[] { 5.123 }, FourOnTheFloorKicks.From(Vouched(new[] { 5.123 })));
    }

    [Fact]
    public void AKickAtTheVeryStart_IsNeverPulledNegative()
    {
        // Every later kick sits 5 ms early against the first, so the first is pulled 5 ms earlier: below zero.
        double[] stored = { 0.001, 0.476, 0.956, 1.436, 1.916 };

        IReadOnlyList<double> smoothed = FourOnTheFloorKicks.From(Vouched(stored));

        Assert.Equal(0.0, smoothed[0]);
    }

    private static BpmResult Vouched(double[] onsets) => new(Bpm, 0.9)
    {
        KickOnsetsSeconds = onsets,
        KickPhaseMarginRatio = 2.0,
        PhaseWindowDisagreementSeconds = 0.003,
    };

    private static double[] Lattice(double first, int count)
        => Enumerable.Range(0, count).Select(k => first + (k * Beat)).ToArray();

    // Whole analysis frames, then the catalog's millisecond rounding — what BpmDetector stores.
    private static double Quantise(double seconds) => Math.Round(Math.Round(seconds / Frame) * Frame, 3);

    private static double MaxError(IReadOnlyList<double> actual, IReadOnlyList<double> truth)
        => actual.Zip(truth, (a, t) => Math.Abs(a - t)).Max();
}
