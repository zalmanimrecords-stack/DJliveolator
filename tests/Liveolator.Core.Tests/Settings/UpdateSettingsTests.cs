using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Settings;

public class UpdateSettingsTests
{
    [Fact]
    public void Default_ChecksOnStartup_AndSkipsNothing()
    {
        Assert.True(UpdateSettings.Default.CheckOnStartup);
        Assert.Null(UpdateSettings.Default.SkippedVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalized_FoldsBlankSkippedVersionToNull(string? skipped)
        => Assert.Null(new UpdateSettings(SkippedVersion: skipped).Normalized().SkippedVersion);

    [Fact]
    public void Normalized_TrimsSkippedVersion()
        => Assert.Equal("0.1.5", new UpdateSettings(SkippedVersion: "  0.1.5 ").Normalized().SkippedVersion);

    [Fact]
    public void AppSettings_Default_IncludesUpdates()
        => Assert.Equal(UpdateSettings.Default, AppSettings.Default.Updates);

    [Fact]
    public void AppSettings_Normalized_NormalizesUpdates()
    {
        AppSettings settings = AppSettings.Default with { Updates = new UpdateSettings(SkippedVersion: "  ") };

        Assert.Null(settings.Normalized().Updates.SkippedVersion);
    }
}
