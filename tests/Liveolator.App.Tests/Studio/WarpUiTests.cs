using Liveolator.App.Features.Studio;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.App.Tests.Studio;

public class WarpUiTests
{
    private const double Tol = 1e-9;

    private static StudioClip Clip(double sourceBpm, bool warp, double lenSec = 60)
        => new(0, "/m/a.wav", 0, System.TimeSpan.Zero, System.TimeSpan.FromSeconds(lenSec),
            SourceBpm: sourceBpm, WarpEnabled: warp);

    // --- clip warp ---

    [Fact]
    public void Warp_Off_FactorIsOne_WidthUnchanged()
    {
        var vm = new StudioClipViewModel(Clip(120, warp: false), null, pixelsPerSecond: 8) { WarpTargetBpm = 140 };
        Assert.Equal(1.0, vm.WarpFactor, Tol);
        Assert.Equal(60 * 8, vm.Width, Tol);
    }

    [Fact]
    public void Warp_On_ShrinksWidthWhenWarpingUp()
    {
        var vm = new StudioClipViewModel(Clip(120, warp: true), null, pixelsPerSecond: 8) { WarpTargetBpm = 140 };
        Assert.Equal(140.0 / 120.0, vm.WarpFactor, Tol);
        Assert.Equal(60.0 / (140.0 / 120.0) * 8, vm.Width, Tol); // plays faster → narrower
    }

    [Fact]
    public void Warp_Badge_ShowsSourceAndTarget()
    {
        var vm = new StudioClipViewModel(Clip(120, warp: true), null, 8) { WarpTargetBpm = 140 };
        Assert.Equal("♪ 120→140", vm.WarpBadge);
    }

    [Fact]
    public void ToClip_PreservesWarpFields()
    {
        var vm = new StudioClipViewModel(Clip(120, warp: true), null, 8);
        StudioClip c = vm.ToClip();
        Assert.Equal(120, c.SourceBpm, Tol);
        Assert.True(c.WarpEnabled);
    }

    // --- tempo lane ---

    [Fact]
    public void TempoLane_ValueBpm_RoundTrip()
    {
        Assert.Equal(130, TempoLaneViewModel.ValueToBpm(0.5), Tol);  // mid of 60..200
        Assert.Equal(0.5, TempoLaneViewModel.BpmToValue(130), Tol);
    }

    [Fact]
    public void TempoLane_ToTempoCurve_MapsPointsToBpm()
    {
        var lane = new TempoLaneViewModel();
        lane.AddPoint(0, 0.5);
        lane.AddPoint(10, 1.0);

        TempoCurve curve = lane.ToTempoCurve();
        Assert.Equal(130, curve.TempoAt(0, 120), Tol);
        Assert.Equal(200, curve.TempoAt(10, 120), Tol);
    }

    [Fact]
    public void TempoLane_Load_MapsBpmToPoints()
    {
        var lane = new TempoLaneViewModel();
        lane.Load(new TempoCurve(new[] { new TempoKeyframe(0, 60), new TempoKeyframe(5, 200) }));

        Assert.Equal(2, lane.Points.Count);
        Assert.Equal(0.0, lane.Points[0].Value, Tol);
        Assert.Equal(1.0, lane.Points[1].Value, Tol);
    }
}
