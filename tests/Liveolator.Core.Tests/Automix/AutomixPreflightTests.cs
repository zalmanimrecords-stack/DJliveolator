using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Automix;
using Xunit;

namespace Liveolator.Core.Tests.Automix;

public class AutomixPreflightTests
{
    private static readonly AutomixSettings Settings = AutomixSettings.Default;

    private static AutomixDeckSnapshot Playing(double bpm = 128.0, double firstBeat = 0.2) => new(
        IsLoaded: true, IsPlaying: true, BaseBpm: bpm, EffectiveBpm: bpm,
        FirstBeatSeconds: firstBeat, PositionSeconds: 30.0, LengthSeconds: 300.0,
        SyncState: SyncLockState.Off, SyncLocked: false);

    private static AutomixDeckSnapshot Loaded(double bpm = 128.0, double firstBeat = 0.2, double length = 300.0)
        => new(
            IsLoaded: true, IsPlaying: false, BaseBpm: bpm, EffectiveBpm: bpm,
            FirstBeatSeconds: firstBeat, PositionSeconds: 0.0, LengthSeconds: length,
            SyncState: SyncLockState.Off, SyncLocked: false);

    private static AutomixPlan Plan(
        AutomixDeckSnapshot from, AutomixDeckSnapshot to, int bars = 16, AutomixStyle style = AutomixStyle.EqMix)
        => AutomixPreflight.Plan(from, 0, to, 1, bars, style, Settings);

    [Fact]
    public void HappyPath_PlansTheRequestedTransition()
    {
        AutomixPlan plan = Plan(Playing(), Loaded());

        Assert.True(plan.IsAllowed);
        Assert.Equal(0, plan.FromSlot);
        Assert.Equal(1, plan.ToSlot);
        Assert.Equal(16, plan.PlannedBars);
        Assert.Equal(AutomixStyle.EqMix, plan.EffectiveStyle);
        Assert.Equal(0.2, plan.MixInSeconds, precision: 9);
    }

    [Fact]
    public void Refuses_WhenTheOutgoingDeckIsNotPlaying()
    {
        AutomixPlan plan = Plan(Loaded(), Loaded());
        Assert.Equal(AutomixRefusal.NothingPlaying, plan.Refusal);
    }

    [Fact]
    public void Refuses_WhenTheIncomingDeckIsEmpty()
    {
        AutomixDeckSnapshot empty = Loaded() with { IsLoaded = false, LengthSeconds = 0.0 };
        Assert.Equal(AutomixRefusal.IncomingNotLoaded, Plan(Playing(), empty).Refusal);
    }

    [Theory]
    [InlineData(0.0, 128.0)]
    [InlineData(128.0, 0.0)]
    public void Refuses_WhenEitherTempoIsUnknown(double fromBpm, double toBpm)
    {
        AutomixDeckSnapshot from = Playing(bpm: fromBpm) with { EffectiveBpm = fromBpm };
        AutomixDeckSnapshot to = Loaded(bpm: toBpm);
        Assert.Equal(AutomixRefusal.TempoUnknown, Plan(from, to).Refusal);
    }

    [Fact]
    public void Refuses_WhenTheFoldedTempoGapExceedsThePitchRange()
    {
        // 100 vs 128 BPM folds to 1.28 — far outside ±8%. Beatmatching would be audible chipmunk.
        Assert.Equal(AutomixRefusal.TempoGapTooLarge, Plan(Playing(bpm: 128.0), Loaded(bpm: 100.0)).Refusal);
    }

    [Fact]
    public void Allows_AHalfTempoPairing_ViaOctaveFolding()
    {
        // 64 vs 128 BPM folds to exactly 1.0 — a valid half-time blend.
        Assert.True(Plan(Playing(bpm: 128.0), Loaded(bpm: 64.0)).IsAllowed);
    }

    [Fact]
    public void AutoShortens_WhenTheOutgoingTrackIsNearItsEnd()
    {
        // 128 BPM 4/4 => 1.875 s/bar. 45 s remain: 16+2 bars = 33.75 s fits; but at 30 s remaining
        // (16+2)*1.875 = 33.75 > 30, so it shortens to 8 bars (10*1.875 = 18.75 s).
        AutomixDeckSnapshot nearEnd = Playing() with { PositionSeconds = 270.0 }; // 30 s remain
        AutomixPlan plan = Plan(nearEnd, Loaded());

        Assert.True(plan.IsAllowed);
        Assert.Equal(8, plan.PlannedBars);
    }

    [Fact]
    public void Refuses_WhenTheOutgoingTrackEndsTooSoonForAnyDetent()
    {
        AutomixDeckSnapshot ending = Playing() with { PositionSeconds = 295.0 }; // 5 s remain
        Assert.Equal(AutomixRefusal.NotEnoughTimeLeft, Plan(ending, Loaded()).Refusal);
    }

    [Fact]
    public void Refuses_WhenTheIncomingTrackCannotCarryTheFloorAfterTheBlend()
    {
        // 16 + 8 headroom bars at 1.875 s/bar = 45 s needed; a 40 s track cannot host the hand-over.
        Assert.Equal(AutomixRefusal.IncomingTooShort, Plan(Playing(), Loaded(length: 40.0)).Refusal);
    }

    [Fact]
    public void DegradesToCrossFade_WhenAGridAnchorIsMissing()
    {
        // No first-beat anchor on the incoming deck: a bass-swap point would be a guess — refuse to
        // guess, blend with the grid-free style instead (advisor S3).
        AutomixPlan plan = Plan(Playing(), Loaded(firstBeat: 0.0), style: AutomixStyle.EqMix);

        Assert.True(plan.IsAllowed);
        Assert.Equal(AutomixStyle.CrossFade, plan.EffectiveStyle);
    }

    [Fact]
    public void KeepsTheRequestedStyle_WhenBothGridsAreKnown()
        => Assert.Equal(AutomixStyle.FxMix, Plan(Playing(), Loaded(), style: AutomixStyle.FxMix).EffectiveStyle);
}
