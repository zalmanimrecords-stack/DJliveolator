using System.Linq;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

/// <summary>
/// Pure checks for the built-in MilkDrop starter preset (doc 28). The GL rendering is verified manually
/// (needs a display); here we pin the contract that makes it loadable and feedback-capable.
/// </summary>
public class MilkdropStarterPresetAddonTests
{
    private static VisualEffectDescriptor Descriptor() =>
        MilkdropStarterPresetAddon.Descriptor("milkdrop-starter.frag");

    [Fact]
    public void Descriptor_IsAGeneratorWithFiveParameters()
    {
        VisualEffectDescriptor descriptor = Descriptor();
        Assert.Equal(VisualEffectRole.Generator, descriptor.Role);
        Assert.Equal(5, descriptor.Parameters.Count);
    }

    [Fact]
    public void Preset_ExposesFiveControllableParameters_WithinTheDocCeiling()
    {
        GeneratorPreset preset = MilkdropStarterPresetAddon.Preset();
        Assert.Equal(5, preset.Controllable.Count);
        Assert.True(preset.Controllable.Count <= GeneratorPreset.MaxControllableParameters);
        Assert.Equal(MilkdropStarterPresetAddon.EffectId, preset.GeneratorEffectId);
    }

    [Fact]
    public void Preset_ExpandsCleanlyAgainstItsOwnDescriptor()
    {
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(
            MilkdropStarterPresetAddon.Preset(), Descriptor(), layerIndex: 0);

        Assert.Equal(5, binding.Macros.Count);
        Assert.All(binding.Macros, m => Assert.Equal(MilkdropStarterPresetAddon.EffectId, m.Target.EffectInstanceId));
        Assert.Contains(binding.Macros, m => m.Name == $"{MilkdropStarterPresetAddon.PresetId}.glow");
    }

    [Fact]
    public void Shader_DeclaresPreviousFrameSampler_SoFeedbackEngages()
        => Assert.Contains("uPreviousFrame", MilkdropStarterPresetAddon.FragmentShader);

    [Fact]
    public void Shader_IsAsciiOnly_ToAvoidIntelPreprocessorEof()
        => Assert.True(MilkdropStarterPresetAddon.FragmentShader.All(ch => ch <= '\x7F'));
}
