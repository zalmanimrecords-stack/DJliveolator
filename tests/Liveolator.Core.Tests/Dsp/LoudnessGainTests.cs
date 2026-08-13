using Liveolator.Core.Dsp;

namespace Liveolator.Core.Tests.Dsp;

public sealed class LoudnessGainTests
{
    private const double TargetLufs = -9.0;

    [Fact]
    public void For_LeavesAnUnmeasuredTrack_AtUnity()
    {
        // Nothing to balance against, and inventing a level would be worse than leaving it alone.
        Assert.Equal(1.0, LoudnessGain.For(null, TargetLufs));
    }

    [Fact]
    public void For_LeavesATrackAlreadyAtTarget_AtUnity()
    {
        Assert.Equal(1.0, LoudnessGain.For(TargetLufs, TargetLufs), precision: 10);
    }

    [Fact]
    public void For_PullsDownATrack_SixDbAboveTarget()
    {
        // A master this hot is the common case in dance music. -6 dB is 0.5012 in amplitude, not exactly
        // 0.5 (that would be -6.0206 dB) — asserting the real figure rather than the rule of thumb.
        Assert.Equal(0.5012, LoudnessGain.For(-3.0, TargetLufs), precision: 4);
    }

    [Fact]
    public void For_BoostsATrack_BelowTarget()
    {
        Assert.True(LoudnessGain.For(-15.0, TargetLufs) > 1.0);
    }

    [Fact]
    public void For_LandsTwoUnequalMasters_AtTheSameLevel()
    {
        // The invariant the whole feature exists for: after gain, a loud record and a quiet one sit level,
        // so the crossfade blends instead of lurching.
        const double LoudLufs = -6.0;
        const double QuietLufs = -13.0;

        double loudPlayback = LoudLufs + Db(LoudnessGain.For(LoudLufs, TargetLufs));
        double quietPlayback = QuietLufs + Db(LoudnessGain.For(QuietLufs, TargetLufs));

        Assert.Equal(loudPlayback, quietPlayback, precision: 6);
        Assert.Equal(TargetLufs, loudPlayback, precision: 6);
    }

    [Fact]
    public void For_ClampsAnAbsurdlyQuietMeasurement_RatherThanSlammingTheLimiter()
    {
        // A near-silent or mis-measured file would otherwise ask for tens of dB of boost, which does not
        // rescue the track — it just pins the master limiter for the whole clip.
        double gain = LoudnessGain.For(-60.0, TargetLufs);

        Assert.True(gain <= LoudnessGain.MaxGain, $"gain {gain} exceeded the clamp");
        Assert.True(gain > 1.0, "a quiet track should still be boosted, just not without limit");
    }

    [Fact]
    public void For_ClampsAnAbsurdlyLoudMeasurement()
    {
        Assert.True(LoudnessGain.For(40.0, TargetLufs) >= LoudnessGain.MinGain);
    }

    private static double Db(double gain) => 20.0 * Math.Log10(gain);
}
