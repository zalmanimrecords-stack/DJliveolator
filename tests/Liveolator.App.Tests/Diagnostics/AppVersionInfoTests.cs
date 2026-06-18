using Liveolator.App.Diagnostics;
using Xunit;

namespace Liveolator.App.Tests.Diagnostics;

public sealed class AppVersionInfoTests
{
    [Fact]
    public void Parse_SplitsVersionAndCommitFromInformationalVersion()
    {
        AppVersionInfo info = AppVersionInfo.Parse("0.1.1+da8d67e", fileVersion: "0.1.1.0");

        Assert.Equal("0.1.1", info.Version);
        Assert.Equal("da8d67e", info.Build);
    }

    [Fact]
    public void Parse_WithoutCommitMetadata_ReportsLocalBuild()
    {
        AppVersionInfo info = AppVersionInfo.Parse("0.1.1", fileVersion: "0.1.1.0");

        Assert.Equal("0.1.1", info.Version);
        Assert.Equal(AppVersionInfo.LocalBuild, info.Build);
    }

    [Fact]
    public void Parse_FallsBackToFileVersionWhenInformationalMissing()
    {
        AppVersionInfo info = AppVersionInfo.Parse(informationalVersion: null, fileVersion: "0.1.1.0");

        Assert.Equal("0.1.1.0", info.Version);
        Assert.Equal(AppVersionInfo.LocalBuild, info.Build);
    }

    [Fact]
    public void Parse_WithNoMetadataAtAll_ReportsUnknown()
    {
        AppVersionInfo info = AppVersionInfo.Parse(informationalVersion: "  ", fileVersion: null);

        Assert.Equal(AppVersionInfo.UnknownVersion, info.Version);
        Assert.Equal(AppVersionInfo.LocalBuild, info.Build);
    }

    [Fact]
    public void Parse_TrailingPlusWithNoCommit_ReportsLocalBuild()
    {
        AppVersionInfo info = AppVersionInfo.Parse("0.1.1+", fileVersion: null);

        Assert.Equal("0.1.1", info.Version);
        Assert.Equal(AppVersionInfo.LocalBuild, info.Build);
    }
}
