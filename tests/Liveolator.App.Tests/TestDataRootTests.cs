using System;
using System.IO;
using Liveolator.Media;
using Xunit;

namespace Liveolator.App.Tests;

public class TestDataRootTests
{
    [Fact]
    public void DefaultRoot_IsATempFolder_NeverTheUsersAppData()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string root = JsonCatalogStore.DefaultRoot();

        Assert.StartsWith(Path.GetTempPath(), root, StringComparison.OrdinalIgnoreCase);
        Assert.False(root.StartsWith(appData, StringComparison.OrdinalIgnoreCase));
    }
}
