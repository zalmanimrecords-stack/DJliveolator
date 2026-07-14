using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public class FrktlPresetFileTests
{
    private const string ValidShader =
        "#version 330 core\nin vec2 vTexCoord;\nout vec4 fragColor;\nuniform sampler2D uPreviousFrame;\n" +
        "uniform float uGlow;\nvoid main(){ fragColor = vec4(uGlow); }";

    private static FrktlPresetParameter Param(string id, string uniform) =>
        new() { Id = id, Uniform = uniform, Label = id.ToUpperInvariant(), Min = 0, Max = 1, Default = 0.5 };

    private static FrktlPresetFile File(params FrktlPresetParameter[] parameters) => new()
    {
        Name = "Aurora",
        Parameters = parameters,
        Shader = ValidShader,
    };

    [Fact]
    public void Validate_AcceptsAWellFormedPreset()
        => Assert.True(FrktlPresetValidator.Validate(File(Param("glow", "uGlow"))).IsValid);

    [Fact]
    public void Validate_RejectsBlankName()
        => Assert.False(FrktlPresetValidator.Validate(File(Param("glow", "uGlow")) with { Name = " " }).IsValid);

    [Fact]
    public void Validate_RejectsMoreThanFiveParameters()
    {
        FrktlPresetParameter[] six = Enumerable.Range(0, 6).Select(i => Param($"p{i}", $"uP{i}")).ToArray();
        // The shader must reference each uniform to pass the later check, so build one that does.
        string shader = "#version 330 core\nout vec4 fragColor;\n" +
            string.Concat(six.Select(p => $"uniform float {p.Uniform};\n")) +
            "void main(){ fragColor = vec4(1.0); }";
        var file = new FrktlPresetFile { Name = "X", Parameters = six, Shader = shader };
        Assert.False(FrktlPresetValidator.Validate(file).IsValid);
    }

    [Fact]
    public void Validate_RejectsDuplicateIdsAndUniforms()
    {
        Assert.False(FrktlPresetValidator.Validate(File(Param("glow", "uGlow"), Param("glow", "uOther"))).IsValid);
        Assert.False(FrktlPresetValidator.Validate(File(Param("a", "uGlow"), Param("b", "uGlow"))).IsValid);
    }

    [Fact]
    public void Validate_RejectsBadRange()
    {
        var bad = Param("glow", "uGlow") with { Min = 1, Max = 0 };
        Assert.False(FrktlPresetValidator.Validate(File(bad)).IsValid);
        var outOfRange = Param("glow", "uGlow") with { Min = 0, Max = 1, Default = 2 };
        Assert.False(FrktlPresetValidator.Validate(File(outOfRange)).IsValid);
    }

    [Fact]
    public void Validate_RejectsShaderMissingADeclaredUniform()
    {
        var file = File(Param("glow", "uGlow")) with
        {
            Shader = "#version 330 core\nout vec4 fragColor;\nvoid main(){ fragColor = vec4(1.0); }",
        };
        Assert.False(FrktlPresetValidator.Validate(file).IsValid);
    }

    [Fact]
    public void Validate_RejectsNonAsciiShader()
    {
        var file = File(Param("glow", "uGlow")) with { Shader = ValidShader + "\n// café" };
        Assert.False(FrktlPresetValidator.Validate(file).IsValid);
    }

    [Fact]
    public void Validate_RejectsShaderWithoutMainOrFragColor()
    {
        Assert.False(FrktlPresetValidator.Validate(File() with { Shader = "uniform float x;" }).IsValid);
    }

    [Fact]
    public void Compile_ProducesAGeneratorDescriptorAndControllablePreset()
    {
        FrktlPresetFile file = File(Param("glow", "uGlow"), Param("warp", "uWarp"));
        FrktlPresetCompiler.Compiled compiled = FrktlPresetCompiler.Compile(
            file, "liveolator.frktl.user/aurora", "liveolator.frktl.user", "cache/aurora.frag");

        Assert.Equal(VisualEffectRole.Generator, compiled.Descriptor.Role);
        Assert.Equal("cache/aurora.frag", compiled.Descriptor.ShaderPath);
        Assert.Equal(2, compiled.Descriptor.Parameters.Count);
        Assert.Equal("uGlow", compiled.Descriptor.Parameters[0].Uniform);

        // Preset id == effect id, controllable mirrors the parameters, expansion lines up with the renderer.
        Assert.Equal("liveolator.frktl.user/aurora", compiled.Preset.PresetId);
        Assert.Equal("liveolator.frktl.user/aurora", compiled.Preset.GeneratorEffectId);
        Assert.Equal(new[] { "GLOW", "WARP" }, compiled.Preset.Controllable.Select(c => c.Label));

        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(
            compiled.Preset, compiled.Descriptor, layerIndex: 0);
        Assert.Equal(2, binding.Macros.Count);
        Assert.Contains(binding.Macros, m => m.Name == "liveolator.frktl.user/aurora.glow");
    }
}
