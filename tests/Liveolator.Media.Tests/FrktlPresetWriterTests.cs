using System;
using System.IO;
using System.Linq;
using Liveolator.Core.Visuals;
using Liveolator.Media.Visuals;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Writing a <c>.frktl</c> preset (doc 29) and reading it back through the folder loader must produce the
/// same preset id — the contract that lets an agent author a preset the app then registers.
/// </summary>
public sealed class FrktlPresetWriterTests : IDisposable
{
    private const string ValidShader =
        "#version 330 core\nin vec2 vTexCoord;\nout vec4 fragColor;\nuniform float uGlow;\n" +
        "void main(){ fragColor = vec4(uGlow); }";

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "liveolator-frktl-writer-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static FrktlPresetFile Preset(string name) => new()
    {
        Name = name,
        Parameters = new[]
        {
            new FrktlPresetParameter { Id = "glow", Uniform = "uGlow", Label = "GLOW", Min = 0, Max = 2, Default = 1 },
        },
        Shader = ValidShader,
    };

    [Fact]
    public void Write_ThenLoad_RoundTripsToTheSamePresetId()
    {
        var writer = new FrktlPresetWriter(_folder);
        FrktlPresetWriteResult written = writer.Write(Preset("Aurora Veil"));

        Assert.True(written.Created);
        Assert.Equal("liveolator.frktl.user/aurora-veil", written.PresetId);
        Assert.True(File.Exists(written.Path));

        var effects = new VisualEffectRegistry();
        var presets = new GeneratorPresetRegistry();
        int count = new FrktlPresetFolderLoader(effects, presets, _folder).Load();

        Assert.Equal(1, count);
        Assert.True(presets.TryGet(written.PresetId!, out GeneratorPreset preset));
        Assert.Equal("Aurora Veil", preset.Name);
        Assert.True(effects.TryGet(written.PresetId!, "1.0.0", out _)); // effect id == preset id
    }

    [Fact]
    public void Write_RejectsInvalidPreset_WithoutWritingAFile()
    {
        var writer = new FrktlPresetWriter(_folder);
        FrktlPresetWriteResult result = writer.Write(Preset("Bad") with { Shader = "not a shader" });

        Assert.False(result.Created);
        Assert.NotNull(result.Error);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*.frktl").Any());
    }

    [Fact]
    public void Write_DoesNotClobber_WhenOverwriteFalse()
    {
        var writer = new FrktlPresetWriter(_folder);
        Assert.True(writer.Write(Preset("Aurora")).Created);

        FrktlPresetWriteResult second = writer.Write(Preset("Aurora"), overwrite: false);
        Assert.False(second.Created);
        Assert.Contains("already exists", second.Error);
    }

    [Fact]
    public void List_ReturnsWrittenPresets()
    {
        var writer = new FrktlPresetWriter(_folder);
        writer.Write(Preset("Aurora Veil"));
        writer.Write(Preset("Tunnel Pulse"));

        var names = writer.List().Select(e => e.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Aurora Veil", "Tunnel Pulse" }, names);
    }
}
