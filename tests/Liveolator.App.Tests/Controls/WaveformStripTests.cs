using Liveolator.App.Controls;
using Xunit;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// Covers the strip's pure click-to-seek math (<see cref="WaveformStrip.FractionFromX"/>); the render
/// itself is visual and verified via the UI-shots harness, not here.
/// </summary>
public sealed class WaveformStripTests
{
    [Theory]
    [InlineData(0, 200, 0.0)]
    [InlineData(100, 200, 0.5)]
    [InlineData(200, 200, 1.0)]
    [InlineData(50, 200, 0.25)]
    public void FractionFromX_MapsClickToTrackFraction(double x, double width, double expected)
    {
        Assert.Equal(expected, WaveformStrip.FractionFromX(x, width), 6);
    }

    [Theory]
    [InlineData(-30, 200, 0.0)]   // click left of the strip clamps to the start
    [InlineData(260, 200, 1.0)]   // click past the end clamps to the end
    public void FractionFromX_ClampsToTheUnitRange(double x, double width, double expected)
    {
        Assert.Equal(expected, WaveformStrip.FractionFromX(x, width), 6);
    }

    [Theory]
    [InlineData(100, 0)]          // unmeasured strip
    [InlineData(100, -5)]
    [InlineData(double.NaN, 200)]
    public void FractionFromX_ReturnsZero_OnDegenerateInput(double x, double width)
    {
        Assert.Equal(0.0, WaveformStrip.FractionFromX(x, width), 6);
    }
}
