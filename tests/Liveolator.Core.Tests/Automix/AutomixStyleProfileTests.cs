using Liveolator.Core.Automix;
using Xunit;

namespace Liveolator.Core.Tests.Automix;

public class AutomixStyleProfileTests
{
    // 16 bars of 4/4 = 64 beats, A (side 0) → B (side 1) — the default transition geometry.
    private static readonly AutomixTransitionShape Shape = new(FromSide: 0.0, ToSide: 1.0, BeatsTotal: 64.0);

    // --- CROSS FADE ---

    [Fact]
    public void CrossFade_TravelsLinearlyFromOutgoingToIncomingExtreme()
    {
        var profile = new CrossFadeProfile();

        Assert.Equal(0.0, profile.Evaluate(0.0, Shape).Crossfader!.Value, precision: 9);
        Assert.Equal(0.5, profile.Evaluate(0.5, Shape).Crossfader!.Value, precision: 9);
        Assert.Equal(1.0, profile.Evaluate(1.0, Shape).Crossfader!.Value, precision: 9);
    }

    [Fact]
    public void CrossFade_TouchesNothingButTheCrossfader()
    {
        AutomixFrame frame = new CrossFadeProfile().Evaluate(0.5, Shape);

        Assert.NotNull(frame.Crossfader);
        Assert.Null(frame.FromLow);
        Assert.Null(frame.FromMid);
        Assert.Null(frame.FromHigh);
        Assert.Null(frame.FromFilter);
        Assert.Null(frame.ToLow);
        Assert.Null(frame.ToMid);
        Assert.Null(frame.ToHigh);
        Assert.Null(frame.ToFilter);
    }

    [Fact]
    public void CrossFade_ReversedSides_TravelsTheOtherWay()
    {
        var reversed = new AutomixTransitionShape(FromSide: 1.0, ToSide: 0.0, BeatsTotal: 64.0);
        var profile = new CrossFadeProfile();

        Assert.Equal(1.0, profile.Evaluate(0.0, reversed).Crossfader!.Value, precision: 9);
        Assert.Equal(0.0, profile.Evaluate(1.0, reversed).Crossfader!.Value, precision: 9);
    }

    // --- EQ MIX ---

    [Fact]
    public void EqMix_IncomingBassIsKilledUntilTheMidpointSwap()
    {
        var profile = new EqMixProfile();

        Assert.Equal(0.0, profile.Evaluate(0.0, Shape).ToLow!.Value, precision: 9);
        Assert.Equal(0.0, profile.Evaluate(0.25, Shape).ToLow!.Value, precision: 9);
        Assert.Equal(0.0, profile.Evaluate(0.5, Shape).ToLow!.Value, precision: 9);
    }

    [Fact]
    public void EqMix_BassSwapsCompletelyWithinOneBeatAfterTheMidpoint()
    {
        var profile = new EqMixProfile();
        double oneBeat = 1.0 / Shape.BeatsTotal;

        AutomixFrame after = profile.Evaluate(0.5 + oneBeat, Shape);
        Assert.Equal(0.5, after.ToLow!.Value, precision: 9);   // incoming bass at unity
        Assert.Equal(0.0, after.FromLow!.Value, precision: 9); // outgoing bass killed
    }

    [Fact]
    public void EqMix_TwoFullBassLinesNeverPlayTogether()
    {
        // The cardinal-sin property: at every progress step the two low bands sum to at most one
        // unity band (0.5 each in knob space; the swap is complementary).
        var profile = new EqMixProfile();
        for (double p = 0.0; p <= 1.0; p += 0.01)
        {
            AutomixFrame frame = profile.Evaluate(p, Shape);
            double sum = frame.FromLow!.Value + frame.ToLow!.Value;
            Assert.InRange(sum, 0.0, 0.5 + 1e-9);
        }
    }

    [Fact]
    public void EqMix_IncomingTopsEnterTuckedAndReachUnityBeforeTheSwap()
    {
        var profile = new EqMixProfile();

        Assert.Equal(0.35, profile.Evaluate(0.0, Shape).ToHigh!.Value, precision: 9);
        Assert.Equal(0.5, profile.Evaluate(0.4, Shape).ToHigh!.Value, precision: 9);
        Assert.Equal(0.5, profile.Evaluate(0.45, Shape).ToMid!.Value, precision: 9);
    }

    [Fact]
    public void EqMix_OutgoingTopsAreFullyOutAtTheEnd()
    {
        AutomixFrame end = new EqMixProfile().Evaluate(1.0, Shape);

        Assert.Equal(0.0, end.FromMid!.Value, precision: 9);
        Assert.Equal(0.0, end.FromHigh!.Value, precision: 9);
        Assert.Equal(1.0, end.Crossfader!.Value, precision: 9);
    }

    [Fact]
    public void EqMix_CrossfaderHoldsCenterThroughTheSwapWindow()
    {
        var profile = new EqMixProfile();

        Assert.Equal(0.5, profile.Evaluate(0.45, Shape).Crossfader!.Value, precision: 9);
        Assert.Equal(0.5, profile.Evaluate(0.55, Shape).Crossfader!.Value, precision: 9);
    }

    // --- FX MIX ---

    [Fact]
    public void FxMix_OutgoingFilterSweepsUpFromCenterToLifted()
    {
        var profile = new FxMixProfile();

        Assert.Equal(0.5, profile.Evaluate(0.1, Shape).FromFilter!.Value, precision: 9);
        Assert.Equal(0.85, profile.Evaluate(0.95, Shape).FromFilter!.Value, precision: 9);
    }

    [Fact]
    public void FxMix_IncomingFilterDescendsToOpenByTheMidpoint()
    {
        var profile = new FxMixProfile();

        Assert.Equal(0.62, profile.Evaluate(0.0, Shape).ToFilter!.Value, precision: 9);
        Assert.Equal(0.5, profile.Evaluate(0.5, Shape).ToFilter!.Value, precision: 9);
        Assert.Equal(0.5, profile.Evaluate(1.0, Shape).ToFilter!.Value, precision: 9);
    }

    [Fact]
    public void FxMix_OutgoingBassIsKilledAtTheMidpointSwap()
    {
        var profile = new FxMixProfile();
        double oneBeat = 1.0 / Shape.BeatsTotal;

        Assert.Equal(0.5, profile.Evaluate(0.49, Shape).FromLow!.Value, precision: 9);
        Assert.Equal(0.0, profile.Evaluate(0.5 + oneBeat, Shape).FromLow!.Value, precision: 9);
    }

    [Fact]
    public void FxMix_CrossfaderCompletesByProgressPointEight()
    {
        var profile = new FxMixProfile();

        Assert.Equal(0.0, profile.Evaluate(0.05, Shape).Crossfader!.Value, precision: 9);
        Assert.Equal(1.0, profile.Evaluate(0.8, Shape).Crossfader!.Value, precision: 9);
        Assert.Equal(1.0, profile.Evaluate(1.0, Shape).Crossfader!.Value, precision: 9);
    }

    // --- shared properties ---

    [Theory]
    [InlineData("crossfade")]
    [InlineData("eqmix")]
    [InlineData("fxmix")]
    public void AllProfiles_EmitOnlyValuesInsideTheNormalizedRange(string style)
    {
        IAutomixStyleProfile profile = style switch
        {
            "eqmix" => new EqMixProfile(),
            "fxmix" => new FxMixProfile(),
            _ => new CrossFadeProfile(),
        };

        for (double p = 0.0; p <= 1.0; p += 0.01)
        {
            AutomixFrame frame = profile.Evaluate(p, Shape);
            foreach (double? v in new[]
            {
                frame.Crossfader, frame.FromLow, frame.FromMid, frame.FromHigh, frame.FromFilter,
                frame.ToLow, frame.ToMid, frame.ToHigh, frame.ToFilter,
            })
            {
                if (v is { } value)
                    Assert.InRange(value, 0.0, 1.0);
            }
        }
    }

    [Theory]
    [InlineData("crossfade")]
    [InlineData("eqmix")]
    [InlineData("fxmix")]
    public void AllProfiles_CrossfaderIsMonotonicTowardTheIncomingDeck(string style)
    {
        IAutomixStyleProfile profile = style switch
        {
            "eqmix" => new EqMixProfile(),
            "fxmix" => new FxMixProfile(),
            _ => new CrossFadeProfile(),
        };

        double previous = 0.0;
        for (double p = 0.0; p <= 1.0; p += 0.01)
        {
            double crossfader = profile.Evaluate(p, Shape).Crossfader!.Value;
            Assert.True(crossfader >= previous - 1e-9, $"crossfader regressed at p={p:F2} for {style}");
            previous = crossfader;
        }
    }
}
