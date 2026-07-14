using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class RgbaImageTests
{
    [Fact]
    public void Validated_returns_self_for_a_matching_buffer()
    {
        var image = new RgbaImage(2, 3, new byte[2 * 3 * 4]);

        Assert.Same(image, image.Validated());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 4)]
    public void Validated_rejects_non_positive_dimensions(int width, int height)
    {
        var image = new RgbaImage(width, height, new byte[16]);

        Assert.Throws<ArgumentException>(() => image.Validated());
    }

    [Fact]
    public void Validated_rejects_a_buffer_that_does_not_match_dimensions()
    {
        var image = new RgbaImage(2, 2, new byte[2 * 2 * 4 - 1]);

        Assert.Throws<ArgumentException>(() => image.Validated());
    }
}
