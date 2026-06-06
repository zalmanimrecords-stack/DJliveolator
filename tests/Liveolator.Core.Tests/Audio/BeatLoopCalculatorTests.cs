using System;
using Liveolator.Core.Audio.Sync;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class BeatLoopCalculatorTests
{
    [Fact]
    public void LengthSeconds_FourBeatsAt120Bpm_IsTwoSeconds()
    {
        // 120 BPM => 0.5 s/beat; four beats => 2 s.
        Assert.Equal(2.0, BeatLoopCalculator.LengthSeconds(4.0, 120.0), precision: 6);
    }

    [Fact]
    public void LengthSeconds_ScalesWithTempo()
    {
        // Same four beats at a faster tempo is a shorter span.
        Assert.Equal(1.5, BeatLoopCalculator.LengthSeconds(4.0, 160.0), precision: 6);
    }

    [Fact]
    public void LengthSeconds_SupportsFractionalBeats()
    {
        // A half-beat loop at 120 BPM is a quarter second.
        Assert.Equal(0.25, BeatLoopCalculator.LengthSeconds(0.5, 120.0), precision: 6);
    }

    [Fact]
    public void Region_StartsAtInPointAndEndsAfterTheBeatLength()
    {
        LoopRegion region = BeatLoopCalculator.Region(startSeconds: 10.0, beats: 8.0, bpm: 120.0);

        Assert.Equal(10.0, region.StartSeconds, precision: 6);
        Assert.Equal(14.0, region.EndSeconds, precision: 6); // 8 beats * 0.5 s = 4 s
        Assert.Equal(4.0, region.LengthSeconds, precision: 6);
    }

    [Fact]
    public void LengthSeconds_BeatsBelowMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatLoopCalculator.LengthSeconds(0.0, 120.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-120.0)]
    public void LengthSeconds_NonPositiveBpm_Throws(double bpm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatLoopCalculator.LengthSeconds(4.0, bpm));
    }

    [Fact]
    public void Region_NegativeStart_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatLoopCalculator.Region(-1.0, 4.0, 120.0));
    }
}
