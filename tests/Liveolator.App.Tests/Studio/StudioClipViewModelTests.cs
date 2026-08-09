using Liveolator.App.Features.Studio;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.App.Tests.Studio;

public class StudioClipViewModelTests
{
    private const double Tol = 1e-9;

    private static StudioClipViewModel Make(double start, double inSec, double? outSec, double pps = 8)
        => new(new StudioClip(1, "/m/a.wav", start, TimeSpan.FromSeconds(inSec),
            outSec is { } o ? TimeSpan.FromSeconds(o) : null), track: null, pixelsPerSecond: pps);

    [Fact]
    public void Duration_FromTrim_AndTimelineEnd()
    {
        StudioClipViewModel clip = Make(start: 10, inSec: 5, outSec: 35); // 30s trimmed span
        Assert.Equal(30, clip.DurationSeconds, Tol);
        Assert.Equal(40, clip.TimelineEndSeconds, Tol);
    }

    [Fact]
    public void X_AndWidth_FollowZoom()
    {
        StudioClipViewModel clip = Make(start: 10, inSec: 0, outSec: 20, pps: 8);
        Assert.Equal(80, clip.X, Tol);    // 10s * 8
        Assert.Equal(160, clip.Width, Tol); // 20s * 8

        clip.PixelsPerSecond = 16;
        Assert.Equal(160, clip.X, Tol);
        Assert.Equal(320, clip.Width, Tol);
    }

    [Fact]
    public void MovingStart_NeverGoesNegative_AndUpdatesEnd()
    {
        StudioClipViewModel clip = Make(start: 5, inSec: 0, outSec: 10);
        clip.TimelineStartSeconds = -3;
        Assert.Equal(0, clip.TimelineStartSeconds, Tol);
        Assert.Equal(10, clip.TimelineEndSeconds, Tol);
    }

    [Fact]
    public void ToClip_ProjectsBackTrimAndPlacement()
    {
        StudioClipViewModel clip = Make(start: 12, inSec: 4, outSec: 24);
        clip.DeckSlot = 1;

        StudioClip projected = clip.ToClip();

        Assert.Equal(1, projected.DeckSlot);
        Assert.Equal(12, projected.TimelineStartSeconds, Tol);
        Assert.Equal(TimeSpan.FromSeconds(4), projected.SourceIn);
        Assert.Equal(TimeSpan.FromSeconds(24), projected.SourceOut);
    }

    [Fact]
    public void OpenEnded_UsesDefaultLength()
    {
        StudioClipViewModel clip = Make(start: 0, inSec: 0, outSec: null);
        Assert.True(clip.DurationSeconds > 0);
        Assert.Null(clip.ToClip().SourceOut);
    }

    [Fact]
    public void Gain_AndFades_SeededFromClip()
    {
        StudioClipViewModel clip = new(
            new StudioClip(1, "/m/a.wav", 0, TimeSpan.Zero, null,
                SourceBpm: 0, WarpEnabled: false, Gain: 0.5, FadeInSeconds: 2, FadeOutSeconds: 3),
            track: null, pixelsPerSecond: 8);

        Assert.Equal(0.5, clip.Gain, Tol);
        Assert.Equal(2, clip.FadeInSeconds, Tol);
        Assert.Equal(3, clip.FadeOutSeconds, Tol);
    }

    [Fact]
    public void Gain_AndFades_DefaultToUnityAndNoFade()
    {
        StudioClipViewModel clip = Make(start: 0, inSec: 0, outSec: 10);
        Assert.Equal(1.0, clip.Gain, Tol);
        Assert.Equal(0, clip.FadeInSeconds, Tol);
        Assert.Equal(0, clip.FadeOutSeconds, Tol);
    }

    [Fact]
    public void Gain_AndFades_FlowIntoToClip()
    {
        StudioClipViewModel clip = Make(start: 0, inSec: 0, outSec: 10);
        clip.Gain = 0.75;
        clip.FadeInSeconds = 1.5;
        clip.FadeOutSeconds = 2.5;

        StudioClip projected = clip.ToClip();

        Assert.Equal(0.75, projected.Gain, Tol);
        Assert.Equal(1.5, projected.FadeInSeconds, Tol);
        Assert.Equal(2.5, projected.FadeOutSeconds, Tol);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(2.0, 2.0)]
    public void Gain_ClampedNonNegative(double set, double expected)
    {
        StudioClipViewModel clip = Make(start: 0, inSec: 0, outSec: 10);
        clip.Gain = set;
        Assert.Equal(expected, clip.Gain, Tol);
    }

    [Fact]
    public void Fades_ClampedNonNegative()
    {
        StudioClipViewModel clip = Make(start: 0, inSec: 0, outSec: 10);
        clip.FadeInSeconds = -5;
        clip.FadeOutSeconds = -5;
        Assert.Equal(0, clip.FadeInSeconds, Tol);
        Assert.Equal(0, clip.FadeOutSeconds, Tol);
    }

    [Fact]
    public void Gain_AndFades_FireBeforeMutationForUndo()
    {
        StudioClipViewModel clip = Make(start: 0, inSec: 0, outSec: 10);
        int calls = 0;
        clip.BeforeMutation = () => calls++;

        clip.Gain = 0.5;
        clip.FadeInSeconds = 1;
        clip.FadeOutSeconds = 2;

        Assert.Equal(3, calls);
    }

    [Fact]
    public void Gain_AndFades_NoMutationWhenUnchanged()
    {
        StudioClipViewModel clip = Make(start: 0, inSec: 0, outSec: 10);
        int calls = 0;
        clip.BeforeMutation = () => calls++;

        clip.Gain = clip.Gain;
        clip.FadeInSeconds = clip.FadeInSeconds;
        clip.FadeOutSeconds = clip.FadeOutSeconds;

        Assert.Equal(0, calls);
    }
}
