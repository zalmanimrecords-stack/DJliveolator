using System;
using System.Linq;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public class GeneratorPresetRegistryTests
{
    private static GeneratorPreset Preset(string presetId, string generatorEffectId = "liveolator.builtin/milkdrop")
        => new(presetId, presetId, generatorEffectId, "1.0.0",
            new[] { new ControllableParameter("glow", "GLOW") });

    [Fact]
    public void ReplacePackage_RegistersPresets_ThenTryGetResolvesById()
    {
        var registry = new GeneratorPresetRegistry();
        GeneratorPreset preset = Preset("liveolator.builtin/milkdrop-starter");

        registry.ReplacePackage("liveolator.builtin", new[] { preset });

        Assert.True(registry.TryGet(preset.PresetId, out GeneratorPreset found));
        Assert.Equal(preset.PresetId, found.PresetId);
        Assert.Single(registry.Presets);
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForUnknownPreset()
    {
        var registry = new GeneratorPresetRegistry();
        Assert.False(registry.TryGet("does-not-exist", out _));
    }

    [Fact]
    public void RemovePackage_DropsItsPresets()
    {
        var registry = new GeneratorPresetRegistry();
        registry.ReplacePackage("pkg.a", new[] { Preset("pkg.a/one") });
        registry.ReplacePackage("pkg.b", new[] { Preset("pkg.b/two") });

        registry.RemovePackage("pkg.a");

        Assert.False(registry.TryGet("pkg.a/one", out _));
        Assert.True(registry.TryGet("pkg.b/two", out _));
        Assert.Single(registry.Presets);
    }

    [Fact]
    public void ReplacePackage_ReplacesPreviousPresetsOfSamePackage()
    {
        var registry = new GeneratorPresetRegistry();
        registry.ReplacePackage("pkg", new[] { Preset("pkg/old") });
        registry.ReplacePackage("pkg", new[] { Preset("pkg/new") });

        Assert.False(registry.TryGet("pkg/old", out _));
        Assert.True(registry.TryGet("pkg/new", out _));
    }

    [Fact]
    public void Publish_Rejects_DuplicatePresetIdAcrossPackages()
    {
        var registry = new GeneratorPresetRegistry();
        registry.ReplacePackage("pkg.a", new[] { Preset("shared/id") });

        Assert.Throws<InvalidOperationException>(
            () => registry.ReplacePackage("pkg.b", new[] { Preset("shared/id") }));
    }
}
