using System;
using System.Collections.Generic;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Visuals;
using Xunit;

namespace Liveolator.Core.Tests.Visuals;

/// <summary>
/// The VisualLoadPreset path (doc 28): the handler resolves the preset + its generator descriptor,
/// expands them, and drives <see cref="IVisualPerformanceEngine.LoadPreset"/>. Unknown / unwired
/// inputs are surfaced and no-op rather than throwing.
/// </summary>
public class VisualLoadPresetActionTests
{
    private const string PackageId = "com.example.vis";
    private const string GeneratorId = "com.example.vis/milkdrop";
    private const string PresetId = "com.example.vis/aurora";

    private readonly FakeVisualPerformanceEngine _engine;
    private readonly VisualEffectRegistry _effects = new();
    private readonly GeneratorPresetRegistry _presets = new();
    private readonly VisualActionHandler _handler;

    public VisualLoadPresetActionTests()
    {
        _engine = new FakeVisualPerformanceEngine(
            new VisualBank("Set", new[]
            {
                new VisualScene("scene", Array.Empty<VisualLayer>(), new Dictionary<string, double>(),
                    TransitionStyle.Cut, BeatBehavior.None),
            }));

        _effects.ReplacePackage(PackageId, new[]
        {
            new VisualEffectDescriptor(
                GeneratorId, "1.0.0", PackageId, "shaders/milkdrop.frag",
                new[]
                {
                    new VisualEffectParameter("glow", "uGlow", 0, 1, 0.5),
                    new VisualEffectParameter("warp", "uWarp", 0, 4, 1.0),
                },
                Role: VisualEffectRole.Generator),
        });
        _presets.ReplacePackage(PackageId, new[]
        {
            new GeneratorPreset(PresetId, "Aurora", GeneratorId, "1.0.0",
                new[] { new ControllableParameter("glow", "GLOW"), new ControllableParameter("warp", "WARP") }),
        });

        _handler = new VisualActionHandler(_engine, logger: null, presets: _presets, effects: _effects);
    }

    private void Load(int slot, string? presetId)
        => _handler.Handle(new PerformanceAction(PerformanceActionKind.VisualLoadPreset, Slot: slot, Argument: presetId));

    [Fact]
    public void LoadPreset_ExpandsAndDrivesTheEngine_WithTheControllableMacros()
    {
        Load(slot: 2, PresetId);

        var loaded = Assert.Single(_engine.LoadedPresets);
        Assert.Equal(2, loaded.Layer);
        Assert.Equal(Quantize.Immediate, loaded.When);
        Assert.Equal(GeneratorId, loaded.Binding.Generator.EffectId);
        // One macro per exposed controllable parameter, namespaced by preset id, targeting the generator.
        Assert.Equal(2, loaded.Binding.Macros.Count);
        Assert.Contains(loaded.Binding.Macros, m => m.Name == $"{PresetId}.glow");
        Assert.All(loaded.Binding.Macros, m => Assert.Equal(GeneratorId, m.Target.EffectInstanceId));
        Assert.All(loaded.Binding.Macros, m => Assert.Equal(2, m.Target.Layer));
    }

    [Fact]
    public void LoadPreset_UnknownPreset_DoesNotCallEngine()
    {
        Load(slot: 0, "com.example.vis/missing");
        Assert.Empty(_engine.LoadedPresets);
    }

    [Fact]
    public void LoadPreset_BlankArgument_DoesNotCallEngine()
    {
        Load(slot: 0, presetId: null);
        Assert.Empty(_engine.LoadedPresets);
    }

    [Fact]
    public void LoadPreset_WhenRegistriesNotWired_DoesNotCallEngine()
    {
        var handler = new VisualActionHandler(_engine); // no registries
        handler.Handle(new PerformanceAction(PerformanceActionKind.VisualLoadPreset, Slot: 0, Argument: PresetId));
        Assert.Empty(_engine.LoadedPresets);
    }
}
