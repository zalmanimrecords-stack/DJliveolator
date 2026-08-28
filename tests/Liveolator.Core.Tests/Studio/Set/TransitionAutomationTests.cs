using Liveolator.Core.Mixer;
using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Set;

namespace Liveolator.Core.Tests.Studio.Set;

/// <summary>
/// At 128 BPM a bar is 1.875 s, so a 16-bar blend runs 30 s and trades its lows over 2 bars (16 / 8) = 3.75 s.
/// These numbers keep the expected keyframe positions readable.
/// </summary>
public sealed class TransitionAutomationTests
{
    private const double TempoBpm = 128.0;
    private const double BarSeconds = 1.875;

    /// <summary>
    /// The worst the summed low band is allowed to dip anywhere in a blend. The hand-over crosses with each
    /// side 7.0 dB down, which sums (two uncorrelated basslines, so in power) to 4.04 dB down as measured
    /// across this blend. The number that matters is that it is a few dB and not infinite: before this fix
    /// both lows were fully killed at the same instant, and this assertion read -∞ dB at the blend midpoint.
    /// </summary>
    private const double MaxLowBandDipDb = 4.1;

    [Fact]
    public void Build_NeverLetsBothLowBandsSitAtZeroTogether()
    {
        // The defect this guards: the outgoing low used to reach full cut at the blend middle and the incoming
        // low only started climbing out of full cut at that same instant, so the low band of the whole mix
        // nulled at the centre of every single join. The trade has to be concurrent.
        const double start = 40.0;
        const double overlap = 16.0 * BarSeconds;
        var windows = new[] { new CrossfadeWindow(0, 1, start, overlap) };

        IReadOnlyList<AutomationLane> lanes = TransitionAutomation.Build(windows, TempoBpm);

        AutomationLane outgoing = Low(lanes, slot: 0);
        AutomationLane incoming = Low(lanes, slot: 1);

        double worstDipDb = 0.0;
        double worstAt = start;
        for (int sample = 0; sample <= 4_000; sample++)
        {
            double at = start + (sample / 4_000.0 * overlap);
            double summedPower =
                Squared(LowBandGain(outgoing.ValueAt(at))) + Squared(LowBandGain(incoming.ValueAt(at)));
            double dipDb = -10.0 * Math.Log10(summedPower);
            if (dipDb > worstDipDb)
            {
                worstDipDb = dipDb;
                worstAt = at;
            }
        }

        Assert.True(
            worstDipDb <= MaxLowBandDipDb,
            $"the summed low band fell {worstDipDb:F2} dB at {worstAt:F3} s, past the {MaxLowBandDipDb} dB floor");

        // And the mechanism behind that, stated directly: ONE window covering both decks, rather than two
        // windows laid end to end.
        Assert.Equal(outgoing.Keyframes[0].TimeSeconds, incoming.Keyframes[0].TimeSeconds, precision: 9);
        Assert.Equal(outgoing.Keyframes[^1].TimeSeconds, incoming.Keyframes[^1].TimeSeconds, precision: 9);
    }

    [Theory]
    [InlineData(4.0, 1.0)]   // shorter than 8 bars: the swap floors at one bar
    [InlineData(8.0, 1.0)]
    [InlineData(16.0, 2.0)]
    [InlineData(24.0, 3.0)]
    [InlineData(32.0, 4.0)]
    [InlineData(64.0, 4.0)]  // and never grows past four, however long the blend runs
    public void Build_DerivesTheSwapLength_FromTheBlend_ClampedToFourBars(double blendBars, double expectedSwapBars)
    {
        var windows = new[] { new CrossfadeWindow(0, 1, StartSeconds: 0.0, OverlapSeconds: blendBars * BarSeconds) };

        IReadOnlyList<AutomationLane> lanes = TransitionAutomation.Build(windows, TempoBpm);

        Assert.Equal(expectedSwapBars, SwapBars(Low(lanes, slot: 0)), precision: 6);
        Assert.Equal(expectedSwapBars, SwapBars(Low(lanes, slot: 1)), precision: 6);
    }

    [Theory]
    [InlineData(8.0)]
    [InlineData(16.0)]
    [InlineData(24.0)]
    [InlineData(32.0)]
    public void Build_CentresTheSwapOnABarLine(double blendBars)
    {
        // The hand-over is a move on a downbeat, so the crossing — where the two lows are level — has to land
        // on a bar line. SetBuildOptions only ever emits blends in whole 8-bar steps, so half a blend is always
        // a whole number of bars; these are every length the arranger can actually produce.
        const double start = 40.0;
        double overlap = blendBars * BarSeconds;
        var windows = new[] { new CrossfadeWindow(0, 1, start, overlap) };

        IReadOnlyList<AutomationLane> lanes = TransitionAutomation.Build(windows, TempoBpm);

        double crossing = start + (overlap / 2.0);
        double barsIn = (crossing - start) / BarSeconds;
        Assert.Equal(Math.Round(barsIn), barsIn, precision: 6);
        Assert.Equal(Low(lanes, slot: 0).ValueAt(crossing), Low(lanes, slot: 1).ValueAt(crossing), precision: 6);
    }

    [Fact]
    public void Build_CutsTheOutgoingLowAllTheWay_NotJustNearlyAllTheWay()
    {
        // A trap worth pinning: cos(pi/2) is 6e-17, not 0, and MixerMath only snaps to a true kill at knob
        // position 0 exactly. Left as-is, the last keyframe would park the outgoing low 24 dB down instead of
        // gone — two basslines, quietly, for the rest of the blend.
        var windows = new[] { new CrossfadeWindow(0, 1, StartSeconds: 0.0, OverlapSeconds: 16.0 * BarSeconds) };

        IReadOnlyList<AutomationLane> lanes = TransitionAutomation.Build(windows, TempoBpm);

        Assert.Equal(0.0, Low(lanes, slot: 0).Keyframes[^1].Value);
        Assert.Equal(0.0, Low(lanes, slot: 1).Keyframes[0].Value);
    }

    [Fact]
    public void Build_SizesTheBassSwap_PerTransition_NotFromTheShortestInTheSet()
    {
        // A set almost always contains one short join (a hard-ending record, or a low-confidence clamp).
        // Sizing every swap from the shortest one lets that single join shorten the bass trade for the whole
        // set, so the long blends stack two basslines for longer than intended — audible as mud.
        var windows = new[]
        {
            new CrossfadeWindow(OutSlot: 0, InSlot: 1, StartSeconds: 0.0, OverlapSeconds: 16.0 * BarSeconds),
            new CrossfadeWindow(OutSlot: 1, InSlot: 0, StartSeconds: 100.0, OverlapSeconds: 2.0 * BarSeconds),
        };

        IReadOnlyList<AutomationLane> lanes = TransitionAutomation.Build(windows, TempoBpm);

        // The 16-bar blend keeps its own 2-bar swap; the 2-bar join beside it gets a 1-bar one (clamped to
        // half its blend), and does not drag the long blend down with it.
        Assert.Equal(2.0, SwapBars(Low(lanes, slot: 0), before: 50.0), precision: 6);
        Assert.Equal(1.0, SwapBars(Low(lanes, slot: 0), after: 50.0), precision: 6);
    }

    [Theory]
    [InlineData(8.0)]
    [InlineData(1.0)]   // below anything the arranger emits, but the clamp must still hold
    public void Build_KeepsTheSwapInsideItsOwnBlend(double blendBars)
    {
        // The clamp itself is right: the swap can never run past the blend it lives in, or the outgoing low
        // would start dropping before the incoming record is even audible.
        const double start = 100.0;
        double overlap = blendBars * BarSeconds;
        var windows = new[] { new CrossfadeWindow(0, 1, start, overlap) };

        IReadOnlyList<AutomationLane> lanes = TransitionAutomation.Build(windows, TempoBpm);

        foreach (AutomationLane lane in lanes.Where(l => l.Target == AutomationTarget.EqLow))
        {
            Assert.All(lane.Keyframes, k => Assert.InRange(k.TimeSeconds, start, start + overlap));
        }
    }

    private static AutomationLane Low(IReadOnlyList<AutomationLane> lanes, int slot)
        => Assert.Single(lanes, l => l.Target == AutomationTarget.EqLow && l.DeckSlot == slot);

    // A deck's lane covers the whole set, so a set with two joins needs the window picked out by time.
    private static double SwapBars(AutomationLane lane, double after = double.MinValue, double before = double.MaxValue)
    {
        double[] times = lane.Keyframes
            .Where(k => k.TimeSeconds > after && k.TimeSeconds < before)
            .Select(k => k.TimeSeconds)
            .ToArray();
        return (times.Max() - times.Min()) / BarSeconds;
    }

    // What the band actually does to the audio, which is the only thing a keyframe value is evidence of. The
    // renderer hands these knob positions to MixerMath.EqBandCoefficients without naming a mode, so the cut
    // half is the default Kill: linear in dB from 0 dB at unity down to MaxCutDb, and silent at the very
    // bottom of the knob. Only the cut half is modelled here; the swap never boosts.
    private static double LowBandGain(double knob)
    {
        if (knob <= 0.0)
            return 0.0;

        double db = (knob - EqBands.Unity) * 2.0 * EqCutMode.Kill.MaxCutDb();
        return Math.Pow(10.0, db / 20.0);
    }

    private static double Squared(double value) => value * value;
}
