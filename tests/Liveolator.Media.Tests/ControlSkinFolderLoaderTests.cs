using System;
using System.IO;
using System.Linq;
using Liveolator.Core.Skins;
using Liveolator.Media.Skins;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// The control-skin folder loader (doc 30): reads valid <c>.ctrlskin</c> files (full palette + derived id),
/// skips malformed/invalid ones without throwing, and returns nothing for a missing folder.
/// </summary>
public sealed class ControlSkinFolderLoaderTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "liveolator-ctrlskin-loader-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Load_MissingFolder_ReturnsEmpty()
        => Assert.Empty(new ControlSkinFolderLoader(_folder).Load());

    [Fact]
    public void Load_ReturnsWrittenSkins_WithIdAndPalette()
    {
        var writer = new ControlSkinWriter(_folder);
        writer.Write(new ControlSkinFile { Name = "Cobalt Knob", Kind = ControlSkinKind.Knob, Accent = "#2F80F6" });

        LoadedControlSkin skin = Assert.Single(new ControlSkinFolderLoader(_folder).Load());
        Assert.Equal("liveolator.control-skins/cobalt-knob", skin.SkinId);
        Assert.Equal("Cobalt Knob", skin.File.Name);
        Assert.Equal("#2F80F6", skin.File.Accent);
    }

    [Fact]
    public void Load_SkipsMalformedFile_WithWarning()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "broken.ctrlskin"), "{ not json");
        new ControlSkinWriter(_folder).Write(new ControlSkinFile { Name = "Good", Kind = ControlSkinKind.Slider, Accent = "#D78A16" });

        string warning = string.Empty;
        var loader = new ControlSkinFolderLoader(_folder, onWarning: w => warning = w);
        LoadedControlSkin skin = Assert.Single(loader.Load());

        Assert.Equal("Good", skin.File.Name);
        Assert.Contains("broken.ctrlskin", warning);
    }
}
