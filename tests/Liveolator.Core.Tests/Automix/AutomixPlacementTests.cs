using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Automix;
using Xunit;

namespace Liveolator.Core.Tests.Automix;

public class AutomixPlacementTests
{
    private static AutomixDeckSnapshot Deck(double firstBeat = 0.0) => new(
        IsLoaded: true, IsPlaying: false, BaseBpm: 128.0, EffectiveBpm: 128.0,
        FirstBeatSeconds: firstBeat, PositionSeconds: 0.0, LengthSeconds: 300.0,
        SyncState: SyncLockState.Off, SyncLocked: false);

    [Fact]
    public void MixIn_UsesTheFirstBeatAnchorWhenKnown()
        => Assert.Equal(0.37, AutomixPlacement.MixInSeconds(Deck(firstBeat: 0.37)), precision: 9);

    [Fact]
    public void MixIn_FallsBackToTrackStartWithoutAnAnchor()
        => Assert.Equal(0.0, AutomixPlacement.MixInSeconds(Deck(firstBeat: 0.0)), precision: 9);

    [Fact]
    public void FitBars_KeepsTheRequestWhenItFits()
    {
        // 120 BPM 4/4 => 2 s per bar. 16 bars + 2 tail = 36 s needed; 60 s remain.
        Assert.Equal(16, AutomixPlacement.FitBars(
            requestedBars: 16, outgoingRemainingSeconds: 60.0, outgoingEffectiveBpm: 120.0,
            beatsPerBar: 4, safetyTailBars: 2));
    }

    [Fact]
    public void FitBars_AutoShortensToTheLargestDetentThatFits()
    {
        // 30 s remain at 2 s/bar: 16+2=36 s does not fit, 8+2=20 s does.
        Assert.Equal(8, AutomixPlacement.FitBars(16, 30.0, 120.0, 4, 2));
    }

    [Fact]
    public void FitBars_RefusesWhenEvenTheShortestDetentDoesNotFit()
    {
        // 7 s remain at 2 s/bar: 2+2=8 s does not fit => 0 (the caller refuses; never race the end).
        Assert.Equal(0, AutomixPlacement.FitBars(16, 7.0, 120.0, 4, 2));
    }

    [Fact]
    public void FitBars_NeverExceedsTheRequest()
    {
        // Plenty of time left, but the performer asked for 4 bars — auto-fitting must not lengthen.
        Assert.Equal(4, AutomixPlacement.FitBars(4, 600.0, 120.0, 4, 2));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-120.0)]
    public void FitBars_UnknownTempo_Refuses(double bpm)
        => Assert.Equal(0, AutomixPlacement.FitBars(16, 60.0, bpm, 4, 2));
}
