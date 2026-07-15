using Liveolator.App.Controls;
using Xunit;

namespace Liveolator.App.Tests.Controls;

/// <summary>
/// Covers the strip's pure math — click-to-seek (<see cref="WaveformStrip.FractionFromX"/>), the
/// peak-hold column sampler (<see cref="WaveformStrip.ColumnPeak"/>) and the hot-kick quantizer
/// (<see cref="WaveformStrip.HotLevel"/>); the render itself is visual and verified via the UI-shots
/// harness, not here.
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
    [InlineData(0.0, -0.05)]  // at the start the window stays centred, extending before the track (drawn empty)
    [InlineData(1.0, 0.95)]   // at the end it extends past the track — the playhead stays dead-centre
    public void VisibleWindow_CentresPastTheTrackEnds_WithoutClamping(double progress, double expectedStart)
    {
        // The playhead stays at screen-centre right to the ends (span/2 either side of progress), so a
        // synced A/B pair keeps its kicks stacked through an intro/outro instead of one deck's needle
        // sliding off-centre when its window would have clamped to the edge.
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

    [Fact]
    public void ColumnPeak_HoldsTheMaxAcrossEveryBucketInTheColumn()
    {
        // 10 buckets squeezed into a 5px strip → each 1px column covers 2 buckets. The kick at index 3
        // must surface in the column that spans buckets 2..3 — never lost to single-index sampling.
        var values = new float[10];
        values[3] = 0.9f;

        float peak = WaveformStrip.ColumnPeak(values, x: 1, step: 1, width: 5, start: 0, span: 1);

        Assert.Equal(0.9f, peak, 3);
    }

    [Fact]
    public void ColumnPeak_OutsideTheData_ClampsToTheEdges()
    {
        var values = new[] { 0.2f, 0.4f };

        // A column past the end of the window still reads the last bucket instead of indexing out.
        float peak = WaveformStrip.ColumnPeak(values, x: 99, step: 1, width: 100, start: 0, span: 1);

        Assert.Equal(0.4f, peak, 3);
    }

    [Fact]
    public void ColumnPeak_EmptyData_ReturnsZero()
    {
        Assert.Equal(0f, WaveformStrip.ColumnPeak(System.Array.Empty<float>(), 1, 1, 100, 0, 1));
    }

    [Theory]
    [InlineData(0.5f, 0)]    // a normal kick keeps the plain band colour
    [InlineData(0.84f, 0)]   // just under the hot threshold
    [InlineData(0.85f, 1)]   // at the threshold the core starts to heat up
    [InlineData(0.93f, 2)]
    [InlineData(1.0f, 3)]    // the hardest kicks burn white-hot
    public void HotLevel_QuantizesKickStrengthIntoCoreHeat(float kick, int expectedLevel)
    {
        Assert.Equal(expectedLevel, WaveformStrip.HotLevel(kick));
    }

    [Theory]
    [InlineData(84.0, 13.0)]    // a LIVE strip: 16% of the height (13.44), rounded to whole pixels
    [InlineData(200.0, 15.0)]   // a tall strip clamps to the max so the comb never balloons
    [InlineData(40.0, 9.0)]     // a short strip clamps up to the min so the comb stays legible
    public void CombHeight_ClampsToAReadableBand(double totalHeight, double expected)
    {
        Assert.Equal(expected, WaveformStrip.CombHeight(totalHeight), 3);
    }

    [Theory]
    [InlineData(10.0, 5.0)]     // a tiny strip: never take more than half, even below the min
    [InlineData(0.0, 0.0)]      // unmeasured strip → no comb
    [InlineData(double.NaN, 0.0)]
    public void CombHeight_NeverEatsTheWholeStrip(double totalHeight, double expected)
    {
        Assert.Equal(expected, WaveformStrip.CombHeight(totalHeight), 3);
    }

    [Theory]
    [InlineData(0, 0, true)]    // offset 0: index 0,4,8 are bar starts (the prior behaviour)
    [InlineData(4, 0, true)]
    [InlineData(2, 0, false)]
    [InlineData(2, 2, true)]    // offset 2: the bar starts on index 2,6,10 (the "one" is two beats in)
    [InlineData(6, 2, true)]
    [InlineData(0, 2, false)]   // index 0 is no longer the downbeat once the one moved
    [InlineData(1, 2, false)]
    public void IsBarDownbeat_MarksTheOneEveryBar_FromTheOffset(int index, int offset, bool expected)
    {
        Assert.Equal(expected, WaveformStrip.IsBarDownbeat(index, offset, beatsPerBar: 4));
    }

    [Fact]
    public void IsBarDownbeat_FoldsAnOutOfRangeOffset()
    {
        // An offset ≥ beatsPerBar folds, so it behaves the same as its remainder (6 ≡ 2 in 4/4).
        Assert.Equal(WaveformStrip.IsBarDownbeat(2, 2, 4), WaveformStrip.IsBarDownbeat(2, 6, 4));
    }
}
