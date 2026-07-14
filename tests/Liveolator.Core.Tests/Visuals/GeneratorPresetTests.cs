using System;
using System.Linq;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

public class GeneratorPresetTests
{
    private static ControllableParameter Param(string id) => new(id, id.ToUpperInvariant());

    private static GeneratorPreset Preset(params ControllableParameter[] controllable)
        => new("liveolator.builtin/starter", "Starter", "liveolator.builtin/generator", "1.0.0", controllable);

    [Fact]
    public void Accepts_UpToFiveControllableParameters()
    {
        GeneratorPreset preset = Preset(Param("glow"), Param("warp"), Param("speed"), Param("zoom"), Param("decay"));
        Assert.Equal(5, preset.Controllable.Count);
    }

    [Fact]
    public void Accepts_ZeroControllableParameters()
    {
        GeneratorPreset preset = Preset();
        Assert.Empty(preset.Controllable);
    }

    [Fact]
    public void Rejects_MoreThanFiveControllableParameters()
    {
        ControllableParameter[] six = Enumerable.Range(0, 6).Select(i => Param($"p{i}")).ToArray();
        Assert.Throws<ArgumentException>(() => Preset(six));
    }

    [Fact]
    public void Rejects_DuplicateControllableIds()
        => Assert.Throws<ArgumentException>(() => Preset(Param("glow"), Param("glow")));

    [Fact]
    public void Rejects_BlankPresetId()
        => Assert.Throws<ArgumentException>(
            () => new GeneratorPreset(" ", "Name", "liveolator.builtin/generator", "1.0.0", Array.Empty<ControllableParameter>()));

    [Fact]
    public void Rejects_BlankGeneratorEffectId()
        => Assert.Throws<ArgumentException>(
            () => new GeneratorPreset("preset", "Name", "", "1.0.0", Array.Empty<ControllableParameter>()));

    [Fact]
    public void GeneratorVersion_DefaultsTo_OneZeroZero_WhenBlank()
    {
        var preset = new GeneratorPreset("preset", "Name", "liveolator.builtin/generator", " ", Array.Empty<ControllableParameter>());
        Assert.Equal("1.0.0", preset.GeneratorVersion);
    }

    [Fact]
    public void ControllableParameter_Rejects_BlankIdOrLabel()
    {
        Assert.Throws<ArgumentException>(() => new ControllableParameter(" ", "GLOW"));
        Assert.Throws<ArgumentException>(() => new ControllableParameter("glow", " "));
    }
}
