using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Beat;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

/// <summary>
/// Observable (off-GPU) behaviour of <see cref="GlVisualPerformanceEngine.LoadPreset"/> (doc 28): a
/// preset installs its controllable macros and places its generator on the target layer, leaving the
/// rest of the scene intact. The actual rendering is verified manually (GL needs a display).
/// </summary>
public class GlVisualPerformanceEnginePresetTests
{
    private const string GeneratorId = "com.example.vis/milkdrop";
    private const string PresetId = "com.example.vis/aurora";

    private static VisualMacro Brightness() => new(
        GlVisualPerformanceEngine.BrightnessMacro, 0, 1, 1.0,
        new MacroTarget(0, GlVisualPerformanceEngine.BrightnessMacro));

    private static VisualBank ImageBank() => new("Set", new[]
    {
        new VisualScene(
            "scene",
            new[]
            {
                new VisualLayer("base", new VisualSourceRef(VisualSourceKind.Image, "base.png"),
                    Array.Empty<EffectRef>(), BlendMode.Normal, 1.0),
            },
            new Dictionary<string, double>(), TransitionStyle.Cut, BeatBehavior.None),
    });

    private static VisualEffectDescriptor GeneratorDescriptor() => new(
        GeneratorId, "1.0.0", "com.example.vis", "shaders/milkdrop.frag",
        new[]
        {
            new VisualEffectParameter("glow", "uGlow", 0, 1, 0.5),
            new VisualEffectParameter("warp", "uWarp", 0, 4, 1.0),
        },
        Role: VisualEffectRole.Generator);

    private static GeneratorPreset Preset() => new(
        PresetId, "Aurora", GeneratorId, "1.0.0",
        new[] { new ControllableParameter("glow", "GLOW") });

    [Fact]
    public void LoadPreset_InstallsControllableMacros_AndPlacesGeneratorOnTheLayer()
    {
        var engine = new GlVisualPerformanceEngine(ImageBank(), Brightness(), new FakeBeatClock());
        long before = engine.CompositionVersion;
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset(), GeneratorDescriptor(), layerIndex: 0);

        engine.LoadPreset(binding, layer: 0, Quantize.Immediate);

        // The controllable macro is now bound (so EffectParameterResolver will drive uGlow), targeting
        // the generator instance the renderer addresses by effect id.
        VisualMacro macro = Assert.Single(engine.Macros, m => m.Name == $"{PresetId}.glow");
        Assert.Equal(GeneratorId, macro.Target.EffectInstanceId);

        // The target layer now renders the generator; the composition changed.
        ResolvedLayer layer0 = engine.CurrentComposition()[0];
        Assert.Equal(VisualSourceKind.Generator, layer0.Source.Kind);
        Assert.Equal(GeneratorId, layer0.Source.Reference);
        Assert.True(engine.CompositionVersion > before);
    }

    [Fact]
    public void LoadPreset_KeepsTheBrightnessMacro()
    {
        var engine = new GlVisualPerformanceEngine(ImageBank(), Brightness(), new FakeBeatClock());
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset(), GeneratorDescriptor(), layerIndex: 0);

        engine.LoadPreset(binding, layer: 0, Quantize.Immediate);

        Assert.Contains(engine.Macros, m => m.Name == GlVisualPerformanceEngine.BrightnessMacro);
    }

    [Fact]
    public void LoadPreset_NegativeLayer_IsIgnored()
    {
        var engine = new GlVisualPerformanceEngine(ImageBank(), Brightness(), new FakeBeatClock());
        GeneratorPresetBinding binding = GeneratorPresetExpansion.Expand(Preset(), GeneratorDescriptor(), layerIndex: 0);

        engine.LoadPreset(binding, layer: -1, Quantize.Immediate);

        // No macro installed, base layer unchanged.
        Assert.DoesNotContain(engine.Macros, m => m.Name == $"{PresetId}.glow");
        Assert.Equal(VisualSourceKind.Image, engine.CurrentComposition()[0].Source.Kind);
    }
}
