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

    [Theory]
    [InlineData(0.5, 0.0)]   // no zoom → whole track
    [InlineData(0.5, 1.0)]   // full zoom → whole track
    [InlineData(0.5, 2.0)]   // out-of-range → whole track
    public void VisibleWindow_ShowsWholeTrack_WhenNotZoomed(double progress, double zoom)
    {
        (double start, double span) = WaveformStrip.VisibleWindow(progress, zoom);
        Assert.Equal(0.0, start, 6);
        Assert.Equal(1.0, span, 6);
    }

    [Fact]
    public void VisibleWindow_CentresOnThePlayhead_WhenZoomed()
    {
        (double start, double span) = WaveformStrip.VisibleWindow(progress: 0.5, zoomWindow: 0.10);
        Assert.Equal(0.45, start, 6); // 0.5 ± 0.05
        Assert.Equal(0.10, span, 6);
    }

    [Theory]
    [InlineData(0.0, 0.0)]    // at the start the window pins to the left edge
    [InlineData(1.0, 0.90)]   // at the end the window pins to the right edge (1 - span)
    public void VisibleWindow_ClampsAtTheTrackEnds(double progress, double expectedStart)
    {
        (double start, double span) = WaveformStrip.VisibleWindow(progress, zoomWindow: 0.10);
        Assert.Equal(expectedStart, start, 6);
        Assert.Equal(0.10, span, 6);
    }

    [Fact]
    public void MarkerX_MapsTheKickAnchorIntoTheVisibleWindow()
    {
        Assert.Equal(50.0, WaveformStrip.MarkerX(0.50, start: 0.45, span: 0.10, width: 100)!.Value, 6);
        Assert.Null(WaveformStrip.MarkerX(0.20, start: 0.45, span: 0.10, width: 100));
    }
}
