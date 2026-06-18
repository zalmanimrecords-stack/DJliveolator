using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class WarpMathTests
{
    private const double Tol = 1e-9;

    private static StudioClip Clip(double sourceBpm, bool warp, double start = 0, double? lenSec = 60)
        => new(0, "/m/a.wav", start, TimeSpan.Zero,
            lenSec is { } l ? TimeSpan.FromSeconds(l) : null, SourceBpm: sourceBpm, WarpEnabled: warp);

    [Fact]
    public void WarpFactor_IsOne_WhenWarpOff_OrSourceBpmUnknown()
    {
        Assert.Equal(1.0, WarpMath.WarpFactorAt(Clip(120, warp: false), TempoCurve.Empty, 140, 0), Tol);
        Assert.Equal(1.0, WarpMath.WarpFactorAt(Clip(0, warp: true), TempoCurve.Empty, 140, 0), Tol);
    }

    [Fact]
    public void WarpFactor_FlatTempo_IsProjectOverSource()
    {
        // 120 BPM source → 140 BPM project = play 1.1667× faster.
        Assert.Equal(140.0 / 120.0, WarpMath.WarpFactorAt(Clip(120, warp: true), TempoCurve.Empty, 140, 0), Tol);
    }

    [Fact]
    public void WarpFactor_FollowsTempoCurve()
    {
        var tempo = new TempoCurve(new[] { new TempoKeyframe(0, 120), new TempoKeyframe(10, 140) });
        // at t=5 tempo=130 → factor 130/120
        Assert.Equal(130.0 / 120.0, WarpMath.WarpFactorAt(Clip(120, warp: true), tempo, 120, 5), Tol);
    }

    [Fact]
    public void WarpedTimelineSeconds_ShrinksWhenWarpingUp()
    {
        Assert.Equal(60.0 / (140.0 / 120.0), WarpMath.WarpedTimelineSeconds(60, 140.0 / 120.0), Tol);
    }

    [Fact]
    public void WarpedTimelineWidth_KnownDuration_UsesStartFactor()
    {
        StudioClip clip = Clip(120, warp: true, start: 0, lenSec: 60);
        double w = WarpMath.WarpedTimelineWidth(clip, TempoCurve.Empty, 140);
        Assert.Equal(60.0 * 120.0 / 140.0, w, Tol); // 60s of 120-BPM source at 140 BPM
    }

    [Fact]
    public void WarpedTimelineWidth_OpenEnded_IsZero()
        => Assert.Equal(0.0, WarpMath.WarpedTimelineWidth(Clip(120, warp: true, lenSec: null), TempoCurve.Empty, 140), Tol);
}
