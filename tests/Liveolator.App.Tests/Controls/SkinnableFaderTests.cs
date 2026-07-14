using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Controls;

public sealed class SkinnableFaderTests
{
    private const double Height = 220;
    private const double Pad = 10;

    [Fact]
    public void Value0_sits_at_the_bottom()
        => Assert.Equal(Height - Pad, SkinnableFader.VerticalThumbCentreY(Height, Pad, 0.0), precision: 6);

    [Fact]
    public void Value1_sits_at_the_top()
        => Assert.Equal(Pad, SkinnableFader.VerticalThumbCentreY(Height, Pad, 1.0), precision: 6);

    [Fact]
    public void Value_half_sits_in_the_middle()
        => Assert.Equal((Height) / 2, SkinnableFader.VerticalThumbCentreY(Height, Pad, 0.5), precision: 6);

    [Theory]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    public void Out_of_range_value_stays_within_the_track(double value)
    {
        double y = SkinnableFader.VerticalThumbCentreY(Height, Pad, value);
        Assert.InRange(y, Pad, Height - Pad);
    }
}
