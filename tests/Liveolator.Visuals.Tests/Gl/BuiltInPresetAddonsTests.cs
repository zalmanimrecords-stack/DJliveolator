using System.Linq;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

/// <summary>
/// Contract checks shared by every built-in controllable preset add-on (doc 28) beyond FRKTL:
/// generator role, the five-knob ceiling, clean macro expansion, and ASCII-only shader source
/// (the Intel "pre-mature EOF" trap). GL rendering itself is verified manually (needs a display).
/// </summary>
public class BuiltInPresetAddonsTests
{
    public static IEnumerable<object[]> Addons() => new[]
    {
        new object[] { ForestJourneyPresetAddon.Descriptor("forest.frag"), ForestJourneyPresetAddon.Preset(), "FOREST" },
        new object[] { StormySeaPresetAddon.Descriptor("storm.frag"), StormySeaPresetAddon.Preset(), "STORM" },
        new object[] { MysticSigilsPresetAddon.Descriptor("sigil.frag"), MysticSigilsPresetAddon.Preset(), "SIGIL" },
        new object[] { CosmicNebulaPresetAddon.Descriptor("nebula.frag"), CosmicNebulaPresetAddon.Preset(), "NEBULA" },
        new object[] { NeonTunnelPresetAddon.Descriptor("tunnel.frag"), NeonTunnelPresetAddon.Preset(), "TUNNEL" },
    };

    [Theory]
    [MemberData(nameof(Addons))]
    public void Descriptor_IsAGenerator_WithKnobsWithinTheDocCeiling(
        VisualEffectDescriptor descriptor, GeneratorPreset preset, string name)
    {
        Assert.Equal(VisualEffectRole.Generator, descriptor.Role);
        Assert.Equal(name, preset.Name);
        Assert.True(preset.Controllable.Count <= GeneratorPreset.MaxControllableParameters);
        Assert.Equal(descriptor.Parameters.Count, preset.Controllable.Count);
        Assert.Equal(descriptor.EffectId, preset.GeneratorEffectId);
    }

    [Theory]
    [MemberData(nameof(Addons))]
    public void Preset_ExpandsCleanlyAgainstItsOwnDescriptor(
        VisualEffectDescriptor descriptor, GeneratorPreset preset, string name)
    {
        _ = name;
        GeneratorPresetBinding binding =
            GeneratorPresetExpansion.Expand(preset, descriptor, layerIndex: 0);

        Assert.Equal(preset.Controllable.Count, binding.Macros.Count);
        Assert.All(binding.Macros, m => Assert.Equal(descriptor.EffectId, m.Target.EffectInstanceId));
    }

    public static IEnumerable<object[]> Shaders() => new[]
    {
        new object[] { ForestJourneyPresetAddon.FragmentShader },
        new object[] { StormySeaPresetAddon.FragmentShader },
        new object[] { MysticSigilsPresetAddon.FragmentShader },
        new object[] { CosmicNebulaPresetAddon.FragmentShader },
        new object[] { NeonTunnelPresetAddon.FragmentShader },
    };

    [Theory]
    [MemberData(nameof(Shaders))]
    public void Shader_IsAsciiOnly_ToAvoidIntelPreprocessorEof(string shader)
        => Assert.True(shader.All(ch => ch <= '\x7F'));
}
