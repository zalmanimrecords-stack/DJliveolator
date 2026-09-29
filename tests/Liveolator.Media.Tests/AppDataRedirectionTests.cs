using Xunit;

namespace Liveolator.Media.Tests;

public sealed class AppDataRedirectionTests
{
    [Fact]
    public void IsPackageRedirect_FlagsAWriteThatLandedInAPackageCache()
    {
        Assert.True(AppDataRedirection.IsPackageRedirect(
            @"C:\Users\DJUSER~1\AppData\Roaming\Liveolator",
            @"C:\Users\DjUserName\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Liveolator\p.tmp"));
    }

    [Fact]
    public void IsPackageRedirect_AcceptsAnUnredirectedPath_EvenWhenSpelledWithShortNames()
    {
        Assert.False(AppDataRedirection.IsPackageRedirect(
            @"C:\Users\DJUSER~1\AppData\Local\Temp\x",
            @"C:\Users\DjUserName\AppData\Local\Temp\x\p.tmp"));
    }

    [Fact]
    public void IsPackageRedirect_IgnoresAFolderThatIsAlreadyInsideAPackage()
    {
        const string packaged = @"C:\Users\u\AppData\Local\Packages\App_1\LocalCache\Roaming\Liveolator";
        Assert.False(AppDataRedirection.IsPackageRedirect(packaged, packaged + @"\p.tmp"));
    }

    [Fact]
    public void DetectRedirect_ReportsNothing_ForATempFolder_AndLeavesNoProbeBehind()
    {
        using var dir = new TempDirectory();

        Assert.Null(AppDataRedirection.DetectRedirect(dir.Path));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }
}
