using Liveolator.App.Features.Studio;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.App.Tests.Studio;

public class AutomationEditingTests
{
    private const double Tol = 1e-9;

    // --- AutomationMath ---

    [Fact]
    public void ValueFromY_IsInverted_TopIsOne()
    {
        Assert.Equal(1.0, AutomationMath.ValueFromY(0, 100), Tol);    // top
        Assert.Equal(0.0, AutomationMath.ValueFromY(100, 100), Tol);  // bottom
        Assert.Equal(0.5, AutomationMath.ValueFromY(50, 100), Tol);   // middle
    }

    [Fact]
    public void YFromValue_RoundTripsValueFromY()
    {
        double y = AutomationMath.YFromValue(0.3, 100);
        Assert.Equal(0.3, AutomationMath.ValueFromY(y, 100), Tol);
    }

    [Fact]
    public void NearestPointIndex_FindsClosestWithinTolerance_ElseMinusOne()
    {
        var pts = new[] { (Time: 0.0, Value: 1.0), (Time: 10.0, Value: 0.0) };
        // point 1 at x=10*8=80, y=YFromValue(0,100)=100. Pointer near it:
        Assert.Equal(1, AutomationMath.NearestPointIndex(pts, 82, 98, pixelsPerSecond: 8, height: 100, tolerancePx: 10));
        // far from any point:
        Assert.Equal(-1, AutomationMath.NearestPointIndex(pts, 400, 50, pixelsPerSecond: 8, height: 100, tolerancePx: 10));
    }

    // --- AutomationLaneViewModel ---

    [Fact]
    public void AddPoint_KeepsTimeOrder()
    {
        var lane = new AutomationLaneViewModel(AutomationTarget.DeckGain, 0);
        lane.AddPoint(10, 1.0);
        lane.AddPoint(2, 0.2);
        lane.AddPoint(6, 0.6);

        Assert.Equal(new[] { 2.0, 6.0, 10.0 }, lane.Points.Select(p => p.TimeSeconds));
    }

    [Fact]
    public void Point_ClampsValueAndTime()
    {
        var p = new AutomationPointViewModel(-5, 2.0);
        Assert.Equal(0, p.TimeSeconds, Tol);
        Assert.Equal(1.0, p.Value, Tol);
        p.Value = -1;
        Assert.Equal(0.0, p.Value, Tol);
    }

    [Fact]
    public void SetPointAt_ReplacesNearbyPoint_AddsDistantOne()
    {
        var lane = new AutomationLaneViewModel(AutomationTarget.DeckGain, 0);
        lane.AddPoint(5.0, 0.2);

        lane.SetPointAt(5.05, 0.9, mergeToleranceSeconds: 0.1); // within tolerance → overwrites
        AutomationPointViewModel only = Assert.Single(lane.Points);
        Assert.Equal(0.9, only.Value, Tol);

        lane.SetPointAt(6.0, 0.3, mergeToleranceSeconds: 0.1);  // beyond tolerance → new point
        Assert.Equal(2, lane.Points.Count);
    }

    [Fact]
    public void ToLane_FromKeyframes_RoundTrips()
    {
        var lane = new AutomationLaneViewModel(AutomationTarget.EqLow, 1, new[]
        {
            new AutomationKeyframe(0, 0.5),
            new AutomationKeyframe(8, 0.0),
        });

        AutomationLane core = lane.ToLane();

        Assert.Equal(AutomationTarget.EqLow, core.Target);
        Assert.Equal(1, core.DeckSlot);
        Assert.Equal(2, core.Keyframes.Count);
        Assert.Equal(0.5, core.ValueAt(0), Tol);
        Assert.Equal(0.0, core.ValueAt(8), Tol);
    }
}
