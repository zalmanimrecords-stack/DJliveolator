using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class TempoCurveTests
{
    private const double Tol = 1e-9;

    private static TempoCurve Curve(params (double t, double bpm)[] keys)
        => new(keys.Select(k => new TempoKeyframe(k.t, k.bpm)).ToList());

    [Fact]
    public void Empty_ReturnsDefaultBpm()
    {
        Assert.Equal(124.0, TempoCurve.Empty.TempoAt(0, 124.0), Tol);
        Assert.Equal(124.0, TempoCurve.Empty.TempoAt(999, 124.0), Tol);
    }

    [Fact]
    public void SingleKeyframe_IsConstant()
    {
        TempoCurve c = Curve((5, 128));
        Assert.Equal(128, c.TempoAt(0, 120), Tol);
        Assert.Equal(128, c.TempoAt(100, 120), Tol);
    }

    [Fact]
    public void BeforeFirst_AndAfterLast_HoldFlat()
    {
        TempoCurve c = Curve((10, 120), (20, 140));
        Assert.Equal(120, c.TempoAt(0, 100), Tol);
        Assert.Equal(140, c.TempoAt(99, 100), Tol);
    }

    [Fact]
    public void BetweenKeyframes_LinearlyInterpolates()
    {
        TempoCurve c = Curve((0, 120), (10, 140));
        Assert.Equal(130, c.TempoAt(5, 0), Tol);   // halfway
        Assert.Equal(125, c.TempoAt(2.5, 0), Tol);
    }

    [Fact]
    public void CoincidentTime_TakesLaterValue()
    {
        TempoCurve c = Curve((0, 120), (5, 120), (5, 150), (10, 150));
        Assert.Equal(150, c.TempoAt(5, 0), Tol);
    }
}
