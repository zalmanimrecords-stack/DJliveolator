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
    public void Presets_AreListedFromTheRegistry()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var vm = new PresetControlsViewModel(presets, effects, new FakeDispatcher());

        PresetOptionViewModel option = Assert.Single(vm.Presets);
        Assert.Equal(PresetId, option.PresetId);
        Assert.Equal("Aurora", option.Name);
    }

    [Fact]
    public void LoadPreset_DispatchesVisualLoadPreset_AndBuildsLabelledKnobs()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher, targetLayer: 2);

        vm.LoadPreset(PresetId);

        PerformanceAction load = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualLoadPreset, load.Kind);
        Assert.Equal(2, load.Slot);
        Assert.Equal(PresetId, load.Argument);

        Assert.Equal(2, vm.Controls.Count);
        Assert.Equal(new[] { "GLOW", "WARP" }, vm.Controls.Select(c => c.Label));
        Assert.Equal(PresetId, vm.ActivePresetId);
        Assert.True(vm.HasControls);
    }

    [Fact]
    public void Knob_EmitsVisualSetMacro_WithTheNamespacedMacroName()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher);
        vm.LoadPreset(PresetId);
        dispatcher.Dispatched.Clear();

        vm.Controls[0].Value = 0.7;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSetMacro, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal($"{PresetId}.glow", action.Argument);
        Assert.Equal(0.7, action.Value);
    }

    [Fact]
    public void SelectingAPreset_LoadsIt()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher);

        vm.SelectedPreset = vm.Presets[0];

        Assert.Equal(PresetId, vm.ActivePresetId);
        Assert.Contains(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.VisualLoadPreset);
    }

    [Fact]
    public void Unwired_IsDisabledAndNeverEmits()
    {
        var vm = new PresetControlsViewModel();

        Assert.False(vm.IsEnabled);
        vm.LoadPreset(PresetId);
        Assert.Empty(vm.Controls);
        Assert.Null(vm.ActivePresetId);
    }

    [Fact]
    public void RefreshPresets_PicksUpPresetsRegisteredAfterConstruction()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var vm = new PresetControlsViewModel(presets, effects, new FakeDispatcher());
        Assert.Single(vm.Presets);

        // A second package is registered after the VM was built (e.g. a folder reload from the UI).
        const string secondId = "com.example.vis2/nebula";
        presets.ReplacePackage("com.example.vis2", new[]
        {
            new GeneratorPreset(secondId, "Nebula", GeneratorId, "1.0.0",
                new[] { new ControllableParameter("glow", "GLOW") }),
        });

        vm.RefreshPresets();

        Assert.Equal(2, vm.Presets.Count);
        Assert.Contains(vm.Presets, option => option.PresetId == secondId);
    }

    [Fact]
    public void RefreshPresets_PreservesSelection_WithoutReDispatchingLoad()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher);
        vm.SelectedPreset = vm.Presets[0];
        dispatcher.Dispatched.Clear();

        vm.RefreshPresets();

        Assert.NotNull(vm.SelectedPreset);
        Assert.Equal(PresetId, vm.SelectedPreset!.PresetId);
        Assert.Equal(PresetId, vm.ActivePresetId);
        // Restoring the selection must not re-load the already-active preset onto the engine.
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public void RefreshPresets_ClearsSelection_WhenItsPresetIsGone()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var vm = new PresetControlsViewModel(presets, effects, new FakeDispatcher());
        vm.SelectedPreset = vm.Presets[0];

        presets.RemovePackage(PackageId);
        vm.RefreshPresets();

        Assert.Empty(vm.Presets);
        Assert.Null(vm.SelectedPreset);
    }

    [Fact]
    public void MacroFeedback_UpdatesMatchingKnob_WithoutRedispatch()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = Registries();
        var dispatcher = new FakeDispatcher();
        var vm = new PresetControlsViewModel(presets, effects, dispatcher);
        vm.LoadPreset(PresetId);
        dispatcher.Dispatched.Clear();

        dispatcher.RaiseFeedback(
            PerformanceActionKind.VisualSetMacro, slot: 0,
            new ActionFeedbackState(false, true, 0.33, $"{PresetId}.warp"));

        Assert.Equal(0.33, vm.Controls[1].Value);
        Assert.Empty(dispatcher.Dispatched);
    }
}
