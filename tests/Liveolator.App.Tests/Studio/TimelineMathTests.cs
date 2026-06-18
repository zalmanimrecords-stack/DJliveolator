using Liveolator.App.Features.Studio;
using Xunit;

namespace Liveolator.App.Tests.Studio;

public class TimelineMathTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void SecondsFromX_AndBack_RoundTrip()
    {
        Assert.Equal(5.0, TimelineMath.SecondsFromX(40, pixelsPerSecond: 8), Tol);
        Assert.Equal(40.0, TimelineMath.XFromSeconds(5, pixelsPerSecond: 8), Tol);
    }

    [Fact]
    public void SecondsFromX_ClampsAtZero_AndHandlesZeroZoom()
    {
        Assert.Equal(0, TimelineMath.SecondsFromX(-20, 8), Tol);
        Assert.Equal(0, TimelineMath.SecondsFromX(40, 0), Tol);
    }

    [Theory]
    [InlineData(5.2, 0.5, 5.0)]
    [InlineData(5.3, 0.5, 5.5)]
    [InlineData(5.4, 0, 5.4)]    // no grid → unsnapped
    [InlineData(-3, 0.5, 0)]     // never negative
    public void Snap_RoundsToGrid(double seconds, double grid, double expected)
        => Assert.Equal(expected, TimelineMath.Snap(seconds, grid), Tol);

    [Fact]
    public void BeatSeconds_FromBpm()
    {
        Assert.Equal(0.5, TimelineMath.BeatSeconds(120), Tol);
        Assert.Equal(0, TimelineMath.BeatSeconds(0), Tol);
    }
}
