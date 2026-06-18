using System;
using System.IO;
using Liveolator.Visuals.Gl;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

/// <summary>
/// Built-in visual caches now live under one Roaming root (%APPDATA%\Liveolator\assets). The legacy
/// Local cache is migrated once so existing shaders/images are not left split across two locations.
/// </summary>
public sealed class VisualAssetPathsTests : IDisposable
{
    private readonly string _old =
        Path.Combine(Path.GetTempPath(), "lv-assets-old", Guid.NewGuid().ToString("N"));
    private readonly string _new =
        Path.Combine(Path.GetTempPath(), "lv-assets-new", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (string dir in new[] { _old, _new })
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TryMigrate_MovesLegacyFiles_AndRemovesOldFolder()
    {
        Directory.CreateDirectory(_old);
        File.WriteAllText(Path.Combine(_old, "vu-meter.frag"), "shader");

        VisualAssetPaths.TryMigrate(_old, _new);

        Assert.True(File.Exists(Path.Combine(_new, "vu-meter.frag")));
        Assert.Equal("shader", File.ReadAllText(Path.Combine(_new, "vu-meter.frag")));
        Assert.False(Directory.Exists(_old)); // consolidated to exactly one place
    }

    [Fact]
    public void TryMigrate_DoesNotOverwriteExistingTarget()
    {
        Directory.CreateDirectory(_old);
        Directory.CreateDirectory(_new);
        File.WriteAllText(Path.Combine(_old, "starter.png"), "OLD");
        File.WriteAllText(Path.Combine(_new, "starter.png"), "CURRENT");

        VisualAssetPaths.TryMigrate(_old, _new);

        Assert.Equal("CURRENT", File.ReadAllText(Path.Combine(_new, "starter.png")));
    }

    [Fact]
    public void TryMigrate_MissingOldFolder_DoesNothing_AndDoesNotThrow()
    {
        VisualAssetPaths.TryMigrate(_old, _new); // _old never created
        // No new root is forced into existence when there is nothing to migrate.
        Assert.False(Directory.Exists(_new));
    }
}
