using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Settings;

/// <summary>
/// Guards the persisted window-layout normalization: the size is clamped to the window minimum, garbage
/// (NaN / non-positive) folds back to a usable window, a blank tab id becomes null, and valid values
/// round-trip untouched.
/// </summary>
public sealed class WindowLayoutSettingsTests
{
    [Fact]
    public void Default_IsFullScreen_FirstTab_DefaultSize()
    {
        WindowLayoutSettings layout = WindowLayoutSettings.Default;

        Assert.Null(layout.ActiveTabId);
        Assert.True(layout.IsFullScreen);
        Assert.Equal(WindowLayoutSettings.DefaultWidth, layout.Width);
        Assert.Equal(WindowLayoutSettings.DefaultHeight, layout.Height);
        Assert.Null(layout.X);
        Assert.Null(layout.Y);
    }

    [Fact]
    public void Normalized_KeepsValidValues()
    {
        var layout = new WindowLayoutSettings(
            ActiveTabId: "DJ", Width: 1600, Height: 900, X: 120, Y: 80, IsFullScreen: false);

        WindowLayoutSettings normalized = layout.Normalized();

        Assert.Equal("DJ", normalized.ActiveTabId);
        Assert.Equal(1600, normalized.Width);
        Assert.Equal(900, normalized.Height);
        Assert.Equal(120, normalized.X);
        Assert.Equal(80, normalized.Y);
        Assert.False(normalized.IsFullScreen);
    }

    [Theory]
    [InlineData(100, 50)]   // below the minimum
    [InlineData(0, 0)]      // non-positive
    [InlineData(-50, -50)]  // negative
    public void Normalized_ClampsTooSmallSizeToTheMinimumOrDefault(double width, double height)
    {
        WindowLayoutSettings normalized =
            new WindowLayoutSettings(Width: width, Height: height).Normalized();

        Assert.True(normalized.Width >= WindowLayoutSettings.MinWidth);
        Assert.True(normalized.Height >= WindowLayoutSettings.MinHeight);
    }

    [Fact]
    public void Normalized_FoldsNaNSizeToDefault()
    {
        WindowLayoutSettings normalized =
            new WindowLayoutSettings(Width: double.NaN, Height: double.NaN).Normalized();

        Assert.Equal(WindowLayoutSettings.DefaultWidth, normalized.Width);
        Assert.Equal(WindowLayoutSettings.DefaultHeight, normalized.Height);
    }

    [Fact]
    public void Normalized_DropsNaNOrInfinitePosition()
    {
        WindowLayoutSettings normalized =
            new WindowLayoutSettings(X: double.NaN, Y: double.PositiveInfinity).Normalized();

        Assert.Null(normalized.X);
        Assert.Null(normalized.Y);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalized_FoldsBlankTabIdToNull(string tabId)
    {
        WindowLayoutSettings normalized = new WindowLayoutSettings(ActiveTabId: tabId).Normalized();

        Assert.Null(normalized.ActiveTabId);
    }
}
