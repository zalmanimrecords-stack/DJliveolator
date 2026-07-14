using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class ClipGainTests
{
    private const double Tol = 1e-9;

    // A clip on deck 0 sounding [start, start+length) with the given gain/fades.
    private static StudioClip Clip(
        double start, double? lengthSeconds, double gain = 1.0, double fadeIn = 0.0, double fadeOut = 0.0)
        => new(0, "/m/a.wav", start, TimeSpan.Zero,
            lengthSeconds is { } l ? TimeSpan.FromSeconds(l) : null,
            Gain: gain, FadeInSeconds: fadeIn, FadeOutSeconds: fadeOut);

    [Fact]
    public void NoGainNoFades_DefaultsToUnityInsideClip()
    {
        StudioClip clip = Clip(start: 0, lengthSeconds: 10);
        Assert.Equal(1.0, ClipGain.EffectiveGainAt(clip, 5), Tol);
    }

    [Fact]
    public void StaticGain_ScalesUniformlyWithNoFades()
    {
        StudioClip clip = Clip(start: 0, lengthSeconds: 10, gain: 0.5);
        Assert.Equal(0.5, ClipGain.EffectiveGainAt(clip, 0), Tol);
        Assert.Equal(0.5, ClipGain.EffectiveGainAt(clip, 5), Tol);
    }

    [Fact]
    public void FadeIn_RampsLinearlyFromZeroToFullGain()
    {
        StudioClip clip = Clip(start: 10, lengthSeconds: 100, gain: 1.0, fadeIn: 4);

        Assert.Equal(0.0, ClipGain.EffectiveGainAt(clip, 10), Tol);  // at start
        Assert.Equal(0.5, ClipGain.EffectiveGainAt(clip, 12), Tol);  // halfway up the ramp
        Assert.Equal(1.0, ClipGain.EffectiveGainAt(clip, 14), Tol);  // top of the ramp
        Assert.Equal(1.0, ClipGain.EffectiveGainAt(clip, 30), Tol);  // well past the ramp
    }

    [Fact]
    public void FadeIn_ScaledByStaticGain()
    {
        StudioClip clip = Clip(start: 0, lengthSeconds: 100, gain: 0.8, fadeIn: 2);
        Assert.Equal(0.4, ClipGain.EffectiveGainAt(clip, 1), Tol); // 0.8 * 0.5
    }

    [Fact]
    public void FadeOut_RampsLinearlyToZeroAtClipEnd()
    {
        StudioClip clip = Clip(start: 0, lengthSeconds: 20, gain: 1.0, fadeOut: 4);

        Assert.Equal(1.0, ClipGain.EffectiveGainAt(clip, 15), Tol); // before the fade-out begins
        Assert.Equal(1.0, ClipGain.EffectiveGainAt(clip, 16), Tol); // top of the ramp (4s before end)
        Assert.Equal(0.5, ClipGain.EffectiveGainAt(clip, 18), Tol); // halfway down
        Assert.Equal(0.0, ClipGain.EffectiveGainAt(clip, 20 - 1e-12), 1e-6); // approaching the end -> 0
    }

    [Fact]
    public void FadeInAndOut_BothApplyInTheirRegions()
    {
        StudioClip clip = Clip(start: 0, lengthSeconds: 20, gain: 1.0, fadeIn: 4, fadeOut: 4);

        Assert.Equal(0.5, ClipGain.EffectiveGainAt(clip, 2), Tol);  // mid fade-in
        Assert.Equal(1.0, ClipGain.EffectiveGainAt(clip, 10), Tol); // plateau
        Assert.Equal(0.5, ClipGain.EffectiveGainAt(clip, 18), Tol); // mid fade-out
    }

    [Fact]
    public void OpenEndedClip_HasFadeInButNoFadeOut()
    {
        // No SourceOut => no known end => the fade-out ramp cannot anchor and is skipped.
        StudioClip clip = Clip(start: 0, lengthSeconds: null, gain: 1.0, fadeIn: 4, fadeOut: 4);

        Assert.Equal(0.5, ClipGain.EffectiveGainAt(clip, 2), Tol);     // fade-in still applies
        Assert.Equal(1.0, ClipGain.EffectiveGainAt(clip, 1000), Tol);  // stays at full gain forever
    }

    [Fact]
    public void OutsideClipWindow_IsSilent()
    {
        StudioClip clip = Clip(start: 10, lengthSeconds: 10, gain: 1.0);

        Assert.Equal(0.0, ClipGain.EffectiveGainAt(clip, 9), Tol);   // before start
        Assert.Equal(0.0, ClipGain.EffectiveGainAt(clip, 20), Tol);  // at the half-open end
        Assert.Equal(0.0, ClipGain.EffectiveGainAt(clip, 25), Tol);  // after end
    }

    [Fact]
    public void NegativeGain_IsClampedToZero()
    {
        StudioClip clip = Clip(start: 0, lengthSeconds: 10, gain: -0.5);
        Assert.Equal(0.0, ClipGain.EffectiveGainAt(clip, 5), Tol);
    }

    [Fact]
    public void OverlappingFades_LongerThanClip_StayNonNegative()
    {
        // Fades longer than the clip: in/out regions overlap, but the result never goes below 0.
        StudioClip clip = Clip(start: 0, lengthSeconds: 4, gain: 1.0, fadeIn: 8, fadeOut: 8);

        double mid = ClipGain.EffectiveGainAt(clip, 2);
        Assert.True(mid >= 0.0 && mid <= 1.0);
    }
}
