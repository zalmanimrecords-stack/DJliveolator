using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class PsyFractalVisualizerAddonTests
{
    [Fact]
    public void Descriptor_DeclaresGeneratorAndClampedControlSurface()
    {
        VisualEffectDescriptor descriptor = PsyFractalVisualizerAddon.Descriptor("psy.frag");

        Assert.Equal(VisualEffectRole.Generator, descriptor.Role);
        Assert.Equal(PsyFractalVisualizerAddon.EffectId, descriptor.EffectId);
        Assert.Equal(
            new[] { "sensitivity", "glow", "complexity", "symmetry", "speed", "palette", "reduced-motion", "quality" },
            descriptor.Parameters.Select(parameter => parameter.Id));
        Assert.Equal(6, descriptor.Parameters.Single(parameter => parameter.Id == "symmetry").Min);
        Assert.Equal(32, descriptor.Parameters.Single(parameter => parameter.Id == "symmetry").Max);
        Assert.Equal(3, descriptor.Parameters.Single(parameter => parameter.Id == "palette").Max);
    }

    [Fact]
    public void Shader_UsesBeatAudioBandsAndAllDeclaredUniforms()
    {
        VisualEffectDescriptor descriptor = PsyFractalVisualizerAddon.Descriptor("psy.frag");

        Assert.Contains("uniform float uBass", PsyFractalVisualizerAddon.FragmentShader);
        Assert.Contains("uniform float uLowMid", PsyFractalVisualizerAddon.FragmentShader);
        Assert.Contains("uniform float uMid", PsyFractalVisualizerAddon.FragmentShader);
        Assert.Contains("uniform float uHigh", PsyFractalVisualizerAddon.FragmentShader);
        Assert.Contains("uniform float uBeatFlash", PsyFractalVisualizerAddon.FragmentShader);
        Assert.All(
            descriptor.Parameters,
            parameter => Assert.Contains($"uniform float {parameter.Uniform}", PsyFractalVisualizerAddon.FragmentShader));
    }

    [Fact]
    public void EnsureShaderCreated_IsIdempotentAndRewritesStaleContent()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"liveolator-psy-{Guid.NewGuid():N}");
        try
        {
            string path = PsyFractalVisualizerAddon.EnsureShaderCreated(directory);
            File.WriteAllText(path, "stale");

            string second = PsyFractalVisualizerAddon.EnsureShaderCreated(directory);

            Assert.Equal(path, second);
            Assert.Equal(PsyFractalVisualizerAddon.FragmentShader, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
