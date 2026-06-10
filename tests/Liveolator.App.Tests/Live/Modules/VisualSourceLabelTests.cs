using Liveolator.App.Features.Live.Modules;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class VisualSourceLabelTests
{
    [Theory]
    [InlineData("liveolator.builtin/vu-meter", "VU Meter")]
    [InlineData("core/vu-meter", "VU Meter")]
    [InlineData("liveolator.builtin.milkdrop/generator", "Milkdrop")]
    [InlineData("com.acme.fireworks/sparkle", "Sparkle")]
    [InlineData("com.acme.fireworks/rgb-split", "RGB Split")]
    [InlineData("kaleidoscope", "Kaleidoscope")]
    public void Humanize_ProducesFriendlyDisplayNames(string effectId, string expected)
        => Assert.Equal(expected, VisualSourceLabel.Humanize(effectId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Humanize_ReturnsEmpty_ForBlankInput(string? effectId)
        => Assert.Equal(string.Empty, VisualSourceLabel.Humanize(effectId));

    [Fact]
    public void Humanize_FallsBackToPackageName_WhenLocalPartIsGenericPlaceholder()
        => Assert.Equal("Milkdrop", VisualSourceLabel.Humanize("liveolator.builtin.milkdrop/generator"));
}
