using System.Linq;
using System.Reactive.Concurrency;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Visuals;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class PresetControlsViewModelTests
{
    private const string PackageId = "com.example.vis";
    private const string GeneratorId = "com.example.vis/generator";
    private const string PresetId = "com.example.vis/aurora";

    public PresetControlsViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
    }

    private static (VisualEffectRegistry Effects, GeneratorPresetRegistry Presets) Registries()
    {
        var effects = new VisualEffectRegistry();
        effects.ReplacePackage(PackageId, new[]
        {
            new VisualEffectDescriptor(
                GeneratorId, "1.0.0", PackageId, "shaders/generator.frag",
                new[]
                {
                    new VisualEffectParameter("glow", "uGlow", 0, 1, 0.5),
                    new VisualEffectParameter("warp", "uWarp", 0, 4, 1.0),
                },
                Role: VisualEffectRole.Generator),
        });
        var presets = new GeneratorPresetRegistry();
        presets.ReplacePackage(PackageId, new[]
        {
            new GeneratorPreset(PresetId, "Aurora", GeneratorId, "1.0.0",
                new[] { new ControllableParameter("glow", "GLOW"), new ControllableParameter("warp", "WARP") }),
        });
        return (effects, presets);
    }

    [Fact]
    public void TryLoadForGeneratorSource_LoadsTheWrappingPreset_AndBuildsLabelledKnobs()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher, targetLayer: 2);

        bool loaded = vm.TryLoadForGeneratorSource(GeneratorId);

        Assert.True(loaded);
        PerformanceAction load = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualLoadPreset, load.Kind);
        Assert.Equal(2, load.Slot);
        Assert.Equal(PresetId, load.Argument);

        Assert.Equal(new[] { "GLOW", "WARP" }, vm.Controls.Select(c => c.Label));
        Assert.Equal(PresetId, vm.ActivePresetId);
        Assert.True(vm.HasControls);
    }

    [Fact]
    public void TryLoadForGeneratorSource_UnknownGenerator_ClearsAndReturnsFalse()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher);
        vm.TryLoadForGeneratorSource(GeneratorId); // load something first
        dispatcher.Dispatched.Clear();

        bool loaded = vm.TryLoadForGeneratorSource("com.example.vis/not-a-preset");

        Assert.False(loaded);
        Assert.Empty(vm.Controls);
        Assert.Null(vm.ActivePresetId);
        Assert.Empty(dispatcher.Dispatched); // clearing must not dispatch
    }

    [Fact]
    public void Knob_EmitsVisualSetMacro_WithTheNamespacedMacroName()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher);
        vm.TryLoadForGeneratorSource(GeneratorId);
        dispatcher.Dispatched.Clear();

        vm.Controls[0].Value = 0.7;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSetMacro, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal($"{PresetId}.glow", action.Argument);
        Assert.Equal(0.7, action.Value);
    }

    [Fact]
    public void ClearControls_RemovesKnobs_AndResetsActivePreset()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var vm = new PresetControlsViewModel(presets, effects, new FakeDispatcher());
        vm.TryLoadForGeneratorSource(GeneratorId);
        Assert.True(vm.HasControls);

        vm.ClearControls();

        Assert.Empty(vm.Controls);
        Assert.Null(vm.ActivePresetId);
        Assert.False(vm.HasControls);
    }

    [Fact]
    public void Unwired_IsDisabledAndNeverLoads()
    {
        var vm = new PresetControlsViewModel();

        Assert.False(vm.IsEnabled);
        Assert.False(vm.TryLoadForGeneratorSource(GeneratorId));
        Assert.Empty(vm.Controls);
        Assert.Null(vm.ActivePresetId);
    }

    [Fact]
    public void MacroFeedback_UpdatesMatchingKnob_WithoutRedispatch()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher);
        vm.TryLoadForGeneratorSource(GeneratorId);
        dispatcher.Dispatched.Clear();

        dispatcher.RaiseFeedback(
            PerformanceActionKind.VisualSetMacro, slot: 0,
            new ActionFeedbackState(false, true, 0.33, $"{PresetId}.warp"));

        Assert.Equal(0.33, vm.Controls[1].Value);
        Assert.Empty(dispatcher.Dispatched);
    }
}
