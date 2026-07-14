using System.Linq;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

/// <summary>
/// Pure checks for the built-in FRKTL preset (doc 28). The GL rendering is verified manually (needs a
/// display); here we pin the contract that makes it loadable and feedback-capable.
/// </summary>
public class FrktlPresetAddonTests
{
    private static VisualEffectDescriptor Descriptor() =>
        FrktlPresetAddon.Descriptor("frktl.frag");

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
        GeneratorPreset preset = FrktlPresetAddon.Preset();
        Assert.Equal(5, preset.Controllable.Count);
        Assert.True(preset.Controllable.Count <= GeneratorPreset.MaxControllableParameters);
        Assert.Equal(FrktlPresetAddon.EffectId, preset.GeneratorEffectId);
        Assert.Equal("FRKTL", preset.Name);
    }

    [Fact]
    public void Preset_ExpandsCleanlyAgainstItsOwnDescriptor()
    {
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(
            FrktlPresetAddon.Preset(), Descriptor(), layerIndex: 0);

        Assert.Equal(5, binding.Macros.Count);
        Assert.All(binding.Macros, m => Assert.Equal(FrktlPresetAddon.EffectId, m.Target.EffectInstanceId));
        Assert.Contains(binding.Macros, m => m.Name == $"{FrktlPresetAddon.PresetId}.glow");
    }

    [Fact]
    public void Shader_DeclaresPreviousFrameSampler_SoFeedbackEngages()
        => Assert.Contains("uPreviousFrame", FrktlPresetAddon.FragmentShader);

    [Fact]
    public void Shader_IsAsciiOnly_ToAvoidIntelPreprocessorEof()
        => Assert.True(FrktlPresetAddon.FragmentShader.All(ch => ch <= '\x7F'));
}
