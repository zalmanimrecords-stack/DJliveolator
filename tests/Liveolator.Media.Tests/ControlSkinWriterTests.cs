using System;
using System.IO;
using System.Linq;
using Liveolator.Core.Skins;
using Liveolator.Media.Skins;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Writing a <c>.ctrlskin</c> (doc 30) derives a predictable id/path, validates before writing, refuses to
/// clobber without overwrite, and lists back what it wrote — the contract the MCP authoring tools rely on.
/// </summary>
public sealed class ControlSkinWriterTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "liveolator-ctrlskin-writer-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static ControlSkinFile Skin(string name) => new()
    {
        Name = name,
        Kind = ControlSkinKind.Knob,
        Accent = "#2F80F6",
    };

    [Fact]
    public void Write_DerivesIdAndPath_AndWritesFile()
    {
        var writer = new ControlSkinWriter(_folder);
        ControlSkinWriteResult written = writer.Write(Skin("Cobalt Knob"));

        Assert.True(written.Created);
        Assert.Equal("liveolator.control-skins/cobalt-knob", written.SkinId);
        Assert.True(File.Exists(written.Path));
        Assert.EndsWith("cobalt-knob.ctrlskin", written.Path);
    }

    [Fact]
    public void Write_RejectsInvalidSkin_WithoutWritingAFile()
    {
        var writer = new ControlSkinWriter(_folder);
        ControlSkinWriteResult result = writer.Write(Skin("Bad") with { Accent = "not-a-colour" });

        Assert.False(result.Created);
        Assert.NotNull(result.Error);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*.ctrlskin").Any());
    }

    [Fact]
    public void Write_DoesNotClobber_WhenOverwriteFalse()
    {
        var writer = new ControlSkinWriter(_folder);
        Assert.True(writer.Write(Skin("Cobalt")).Created);

        ControlSkinWriteResult second = writer.Write(Skin("Cobalt"), overwrite: false);
        Assert.False(second.Created);
        Assert.Contains("already exists", second.Error);
    }

    [Fact]
    public void List_ReturnsWrittenSkins_WithKind()
    {
        var writer = new ControlSkinWriter(_folder);
        writer.Write(Skin("Cobalt Knob"));
        writer.Write(new ControlSkinFile { Name = "Amber Slider", Kind = ControlSkinKind.Slider, Accent = "#D78A16" });

        var entries = writer.List().OrderBy(e => e.Name).ToArray();
        Assert.Equal(new[] { "Amber Slider", "Cobalt Knob" }, entries.Select(e => e.Name).ToArray());
        Assert.Equal(ControlSkinKind.Slider, entries[0].Kind);
    }
}
