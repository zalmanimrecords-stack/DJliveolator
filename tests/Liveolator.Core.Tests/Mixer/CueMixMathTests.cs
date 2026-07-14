using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Core.Tests.Mixer;

public class CueMixMathTests
{
    private const double Tol = 1e-9;

    private static MixerState WithCue(bool cueA, bool cueB)
    {
        MixerState state = MixerState.Default;
        state = state.WithChannel(MixerState.DeckA, state.Channel(MixerState.DeckA) with { CueEnabled = cueA });
        state = state.WithChannel(MixerState.DeckB, state.Channel(MixerState.DeckB) with { CueEnabled = cueB });
        return state;
    }

    // --- Pre-fade send: independent of crossfader / channel gain ---

    [Fact]
    public void DeckCueSendGain_IsOne_WhenCued_RegardlessOfCrossfader()
    {
        MixerState state = WithCue(cueA: true, cueB: false)
            .WithCrossfader(1.0); // crossfader fully on B would silence A in the master...

        // ...but the PFL send is pre-fade, so deck A still feeds the cue at unity.
        Assert.Equal(1.0, CueMixMath.DeckCueSendGain(state, MixerState.DeckA), Tol);
        Assert.Equal(0.0, CueMixMath.DeckCueSendGain(state, MixerState.DeckB), Tol);
    }

    [Fact]
    public void DeckCueSendGain_IsZero_WhenNotCued()
    {
        MixerState state = WithCue(cueA: false, cueB: false);

        Assert.Equal(0.0, CueMixMath.DeckCueSendGain(state, MixerState.DeckA), Tol);
        Assert.Equal(0.0, CueMixMath.DeckCueSendGain(state, MixerState.DeckB), Tol);
    }

    [Fact]
    public void AnyDeckCued_ReflectsRouting()
    {
        Assert.False(CueMixMath.AnyDeckCued(WithCue(false, false)));
        Assert.True(CueMixMath.AnyDeckCued(WithCue(true, false)));
        Assert.True(CueMixMath.AnyDeckCued(WithCue(false, true)));
    }

    // --- Cue/master blend (equal-power) ---

    [Fact]
    public void BlendGains_FullCue_IsAllCueNoMaster()
    {
        (double cue, double master) = CueMixMath.BlendGains(CueBusState.FullCue);

        Assert.Equal(1.0, cue, Tol);
        Assert.Equal(0.0, master, Tol);
    }

    [Fact]
    public void BlendGains_FullMaster_IsAllMasterNoCue()
    {
        (double cue, double master) = CueMixMath.BlendGains(CueBusState.FullMaster);

        Assert.Equal(0.0, cue, Tol);
        Assert.Equal(1.0, master, Tol);
    }

    [Fact]
    public void BlendGains_Center_IsEqualPower()
    {
        (double cue, double master) = CueMixMath.BlendGains(0.5);

        Assert.Equal(System.Math.Sqrt(0.5), cue, 1e-6);
        Assert.Equal(System.Math.Sqrt(0.5), master, 1e-6);
        // Equal-power invariant across the whole sweep: cue^2 + master^2 == 1.
        Assert.Equal(1.0, (cue * cue) + (master * master), 1e-9);
    }

    [Theory]
    [InlineData(-0.5, 1.0, 0.0)]
    [InlineData(1.5, 0.0, 1.0)]
    public void BlendGains_ClampsKnobToRange(double knob, double expectedCue, double expectedMaster)
    {
        (double cue, double master) = CueMixMath.BlendGains(knob);

        Assert.Equal(expectedCue, cue, 1e-6);
        Assert.Equal(expectedMaster, master, 1e-6);
    }

    // --- Headphone output gains: blend scaled by level ---

    [Fact]
    public void HeadphoneOutputGains_ScalesBlendByLevel()
    {
        var bus = new CueBusState(Level: 0.5, Mix: CueBusState.FullCue);

        (double cue, double master) = CueMixMath.HeadphoneOutputGains(bus);

        Assert.Equal(0.5, cue, Tol);   // full-cue blend (1.0) × level 0.5
        Assert.Equal(0.0, master, Tol);
    }

    [Fact]
    public void HeadphoneOutputGains_ZeroLevel_SilencesBoth()
    {
        var bus = new CueBusState(Level: 0.0, Mix: 0.5);

        (double cue, double master) = CueMixMath.HeadphoneOutputGains(bus);

        Assert.Equal(0.0, cue, Tol);
        Assert.Equal(0.0, master, Tol);
    }

    // --- Per-deck cue contribution into the headphone mix (A2: audible PFL) ---

    [Fact]
    public void DeckCueContributionGain_IsBusCueGain_WhenCued()
    {
        // A cued deck feeds the headphone mix at the bus cue-leg gain (level-scaled blend cue leg).
        Assert.Equal(0.75, CueMixMath.DeckCueContributionGain(deckCueEnabled: true, cueGain: 0.75), Tol);
    }

    [Fact]
    public void DeckCueContributionGain_IsZero_WhenNotCued()
    {
        // A non-cued deck never bleeds into the headphones, whatever the blend knob.
        Assert.Equal(0.0, CueMixMath.DeckCueContributionGain(deckCueEnabled: false, cueGain: 1.0), Tol);
    }

    [Fact]
    public void DeckCueContributionGain_ClampsNegativeCueGainToZero()
    {
        Assert.Equal(0.0, CueMixMath.DeckCueContributionGain(deckCueEnabled: true, cueGain: -0.3), Tol);
    }
}
