using System;
using System.IO;
using System.Linq;
using Liveolator.Core.Visuals;
using Liveolator.Media.Visuals;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// The folder of <c>.frktl</c> presets (doc 29): valid files register as generator effects + controllable
/// presets with their shader extracted to a cache <c>.frag</c>; invalid files are skipped, not fatal.
/// </summary>
public sealed class FrktlPresetFolderLoaderTests : IDisposable
{
    private const string ValidShader =
        "#version 330 core\nin vec2 vTexCoord;\nout vec4 fragColor;\nuniform sampler2D uPreviousFrame;\n" +
        "uniform float uGlow;\nvoid main(){ fragColor = vec4(uGlow); }";

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "liveolator-frktl-folder-tests", Guid.NewGuid().ToString("N"));

    public FrktlPresetFolderLoaderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private void WriteFile(string fileName, string json) => File.WriteAllText(Path.Combine(_folder, fileName), json);

    private static string ValidJson(string name = "Aurora Veil") => $$"""
        {
          "name": "{{name}}",
          "parameters": [
            { "id": "glow", "uniform": "uGlow", "label": "GLOW", "min": 0.0, "max": 2.0, "default": 1.0 }
          ],
          "shader": {{System.Text.Json.JsonSerializer.Serialize(ValidShader)}}
        }
        """;

    [Fact]
    public void Load_RegistersValidPresets_AndExtractsTheShader()
    {
        WriteFile("aurora-veil.frktl", ValidJson());
        var effects = new VisualEffectRegistry();
        var presets = new GeneratorPresetRegistry();

        int count = new FrktlPresetFolderLoader(effects, presets, _folder).Load();

        Assert.Equal(1, count);
        const string effectId = "liveolator.frktl.user/aurora-veil";
        Assert.True(effects.TryGet(effectId, "1.0.0", out VisualEffectDescriptor descriptor));
        Assert.Equal(VisualEffectRole.Generator, descriptor.Role);
        Assert.True(presets.TryGet(effectId, out GeneratorPreset preset));
        Assert.Equal("Aurora Veil", preset.Name);
        // The shader was written to the cache so GeneratorPass can read it.
        Assert.True(File.Exists(descriptor.ShaderPath));
        Assert.Contains("uGlow", File.ReadAllText(descriptor.ShaderPath));
    }

    [Fact]
    public void Load_KeepsPresetRegistered_WhenShaderCacheCannotBeRefreshed()
    {
        // Regression: a second running instance holding the cache .frag open used to make the folder
        // loader skip the preset (the WriteAllText threw and the whole file was dropped), so presets
        // "disappeared" from the picker whenever two app windows overlapped. The loader must now fall
        // back to the existing cached shader and keep the preset registered.
        WriteFile("aurora-veil.frktl", ValidJson());
        var effects = new VisualEffectRegistry();
        var presets = new GeneratorPresetRegistry();
        Assert.Equal(1, new FrktlPresetFolderLoader(effects, presets, _folder).Load());

        string fragPath = Path.Combine(_folder, ".cache", "aurora-veil.frag");
        // Hold the cache file so it cannot be overwritten (mirrors another instance's compositor on Windows).
        using var hold = new FileStream(fragPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var warnings = new System.Collections.Generic.List<string>();
        int count = new FrktlPresetFolderLoader(effects, presets, _folder, warnings.Add).Load();

        Assert.Equal(1, count);
        Assert.True(presets.TryGet("liveolator.frktl.user/aurora-veil", out _));
    }

    [Fact]
    public void Load_SkipsInvalidFiles_ButKeepsValidOnes()
    {
        WriteFile("good.frktl", ValidJson("Good"));
        WriteFile("bad.frktl", """{ "name": "Bad", "parameters": [], "shader": "not a shader" }""");
        WriteFile("broken.frktl", "{ this is not json");
        var effects = new VisualEffectRegistry();
        var presets = new GeneratorPresetRegistry();
        var warnings = new System.Collections.Generic.List<string>();

        int count = new FrktlPresetFolderLoader(effects, presets, _folder, warnings.Add).Load();

        Assert.Equal(1, count);
        Assert.True(presets.TryGet("liveolator.frktl.user/good", out _));
        Assert.Equal(2, warnings.Count); // bad + broken were reported
    }

    [Fact]
    public void Load_MissingFolder_ReturnsZero_AndDoesNotThrow()
    {
        string missing = Path.Combine(_folder, "does-not-exist-yet");
        var effects = new VisualEffectRegistry();
        var presets = new GeneratorPresetRegistry();

        int count = new FrktlPresetFolderLoader(effects, presets, missing).Load();

        Assert.Equal(0, count);
        // The loader creates the folder on demand so the operator has somewhere to drop files.
        Assert.True(Directory.Exists(missing));
    }
}
