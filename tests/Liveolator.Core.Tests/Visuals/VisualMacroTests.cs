using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public class VisualMacroTests
{
    private static VisualMacro Macro(double min, double max, double @default)
        => new("intensity", min, max, @default, new MacroTarget(0, "opacity"));

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    public void Resolve_MapsNormalizedAcrossUnitRange(double normalized, double expected)
        => Assert.Equal(expected, Macro(0, 1, 0).Resolve(normalized), precision: 9);

    [Fact]
    public void Resolve_MapsIntoArbitraryRange()
    {
        // speed 0.5x..2x, midpoint 0.5 → 1.25x
        VisualMacro speed = Macro(0.5, 2.0, 1.0);
        Assert.Equal(1.25, speed.Resolve(0.5), precision: 9);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void Resolve_ClampsInputToUnitRange(double normalized, double expectedClampedToMinMax)
        => Assert.Equal(expectedClampedToMinMax, Macro(0, 1, 0).Resolve(normalized), precision: 9);

    [Fact]
    public void Constructor_RejectsMaxBelowMin()
        => Assert.Throws<ArgumentException>(() => Macro(1.0, 0.0, 0.5));

    [Fact]
    public void Constructor_RejectsDefaultOutsideRange()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Macro(0.0, 1.0, 2.0));
}
