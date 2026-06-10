using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Settings;

public class AddonSettingsTests
{
    [Fact]
    public void Default_HasNoCustomVuMeterFace()
        => Assert.Null(AddonSettings.Default.VuMeterBackgroundImagePath);

    [Fact]
    public void Default_AppSettingsCarriesDefaultAddons()
        => Assert.Null(AppSettings.Default.Addons.VuMeterBackgroundImagePath);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalized_BlankPathFoldsToNull(string path)
        => Assert.Null(new AddonSettings(path).Normalized().VuMeterBackgroundImagePath);

    [Fact]
    public void Normalized_TrimsPath()
        => Assert.Equal(
            @"C:\faces\brass.png",
            new AddonSettings(@"  C:\faces\brass.png  ").Normalized().VuMeterBackgroundImagePath);

    [Fact]
    public void Normalized_PreservesARealPath()
        => Assert.Equal(
            "/home/dj/face.png",
            new AddonSettings("/home/dj/face.png").Normalized().VuMeterBackgroundImagePath);

    [Fact]
    public void AppSettings_Normalized_NormalizesAddons()
    {
        AppSettings settings = AppSettings.Default with { Addons = new AddonSettings("  x.png  ") };

        Assert.Equal("x.png", settings.Normalized().Addons.VuMeterBackgroundImagePath);
    }
}
