using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Controls;

public sealed class KnobSkinTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 64)]
    [InlineData(0.5, 32)]
    [InlineData(0.25, 16)]
    [InlineData(0.99, 63)]
    public void FrameIndex_maps_value_to_frame(double value, int expected)
        => Assert.Equal(expected, KnobSkin.FrameIndexFor(value, frameCount: 65));

    [Theory]
    [InlineData(-1.0, 0)]
    [InlineData(2.0, 64)]
    [InlineData(double.NaN, 0)]
    public void FrameIndex_clamps_out_of_range(double value, int expected)
        => Assert.Equal(expected, KnobSkin.FrameIndexFor(value, frameCount: 65));

    [Fact]
    public void FrameIndex_single_frame_strip_is_always_zero()
    {
        Assert.Equal(0, KnobSkin.FrameIndexFor(0.0, frameCount: 1));
        Assert.Equal(0, KnobSkin.FrameIndexFor(1.0, frameCount: 1));
    }
}
