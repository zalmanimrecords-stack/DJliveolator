using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public class GeneratorPresetExpansionTests
{
    private const string GeneratorId = "liveolator.builtin/generator";
    private const string PresetId = "liveolator.builtin/starter";
    private const string InstanceId = "gen-instance-1";

    private static VisualEffectDescriptor Generator() => new(
        GeneratorId,
        "1.0.0",
        "liveolator.builtin",
        "shaders/generator.frag",
        new[]
        {
            new VisualEffectParameter("glow", "uGlow", 0.0, 1.0, 0.5),
            new VisualEffectParameter("speed", "uSpeed", 0.5, 2.0, 1.0),
            new VisualEffectParameter("warp", "uWarp", 0.0, 4.0, 1.0),
        },
        Role: VisualEffectRole.Generator);

    private static GeneratorPreset Preset(params string[] controllableIds)
        => new(PresetId, "Starter", GeneratorId, "1.0.0",
            controllableIds.Select(id => new ControllableParameter(id, id.ToUpperInvariant())).ToArray());

    [Fact]
    public void Expand_ProducesOneMacroPerControllableParameter()
    {
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset("glow", "speed"), Generator(), layerIndex: 1, InstanceId);
        Assert.Equal(2, binding.Macros.Count);
    }

    [Fact]
    public void Expand_MacroTargets_PointAtGeneratorInstance()
    {
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset("glow"), Generator(), layerIndex: 2, InstanceId);
        MacroTarget target = binding.Macros[0].Target;
        Assert.Equal(2, target.Layer);
        Assert.Equal(InstanceId, target.EffectInstanceId);
        Assert.Equal("glow", target.Parameter);
    }

    [Fact]
    public void Expand_MacroRanges_MatchDescriptorParameter()
    {
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset("speed"), Generator(), layerIndex: 0, InstanceId);
        VisualMacro macro = binding.Macros[0];
        Assert.Equal(0.5, macro.Min);
        Assert.Equal(2.0, macro.Max);
        Assert.Equal(1.0, macro.Default);
    }

    [Fact]
    public void Expand_MacroNames_AreNamespacedByPresetAndParameter()
    {
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset("glow"), Generator(), layerIndex: 0, InstanceId);
        Assert.Equal($"{PresetId}.glow", binding.Macros[0].Name);
        Assert.Equal($"{PresetId}.glow", GeneratorPresetExpansion.MacroName(PresetId, "glow"));
    }

    [Fact]
    public void Expand_InitialMacroValues_AreNormalizedDescriptorDefaults()
    {
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset("glow", "speed"), Generator(), layerIndex: 0, InstanceId);

        // glow default 0.5 in [0,1] -> normalized 0.5
        Assert.Equal(0.5, binding.InitialMacroValues[$"{PresetId}.glow"], precision: 9);
        // speed default 1.0 in [0.5,2.0] -> normalized (1.0-0.5)/1.5 = 0.3333...
        Assert.Equal(1.0 / 3.0, binding.InitialMacroValues[$"{PresetId}.speed"], precision: 9);
    }

    [Fact]
    public void Expand_GeneratorRef_CarriesInstanceAndAllParameterDefaults()
    {
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset("glow"), Generator(), layerIndex: 0, InstanceId);
        EffectRef generatorRef = binding.Generator;

        Assert.Equal(GeneratorId, generatorRef.EffectId);
        Assert.Equal(InstanceId, generatorRef.InstanceId);
        // Defaults cover every descriptor parameter (not only the controllable ones), keyed by parameter id.
        Assert.Equal(0.5, generatorRef.Defaults["glow"]);
        Assert.Equal(1.0, generatorRef.Defaults["speed"]);
        Assert.Equal(1.0, generatorRef.Defaults["warp"]);
    }

    [Fact]
    public void Expand_Throws_WhenControllableIdNotDeclaredByGenerator()
        => Assert.Throws<ArgumentException>(
            () => GeneratorPresetExpansion.Expand(Preset("nonexistent"), Generator(), layerIndex: 0, InstanceId));

    [Fact]
    public void Expand_Throws_WhenPresetGeneratorIdMismatchesDescriptor()
    {
        var preset = new GeneratorPreset(PresetId, "X", "some.other/generator", "1.0.0",
            new[] { new ControllableParameter("glow", "GLOW") });
        Assert.Throws<ArgumentException>(() => GeneratorPresetExpansion.Expand(preset, Generator(), layerIndex: 0, InstanceId));
    }

    [Fact]
    public void Expand_Throws_WhenDescriptorIsNotAGenerator()
    {
        var effect = Generator() with { Role = VisualEffectRole.Effect };
        Assert.Throws<ArgumentException>(() => GeneratorPresetExpansion.Expand(Preset("glow"), effect, layerIndex: 0, InstanceId));
    }
}
