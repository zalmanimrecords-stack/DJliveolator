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
        clip.DeckSlot = 3;

        StudioClip projected = clip.ToClip();

        Assert.Equal(3, projected.DeckSlot);
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
}
