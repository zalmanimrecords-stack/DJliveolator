using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Extensions;
using Liveolator.Core.Visuals;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class VisualControlViewModelTests
{
    private sealed class FakeCatalog : IExtensionCatalog
    {
        public IReadOnlyList<InstalledExtension> Installed { get; set; } = Array.Empty<InstalledExtension>();
        public int RefreshCount { get; private set; }
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInstaller : IExtensionInstaller
    {
        public (string PackageId, string Version, bool Enabled)? Toggle { get; private set; }
        public Task SetEnabledAsync(
            string packageId,
            string version,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            Toggle = (packageId, version, enabled);
            return Task.CompletedTask;
        }

        public Task<ExtensionInstallPreview> PreviewAsync(
            string packagePath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstalledExtension> InstallAsync(
            string packagePath,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UninstallAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeReloader : IExtensionContentReloader
    {
        public int ReloadCount { get; private set; }
        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            ReloadCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEffectRegistry : IVisualEffectRegistry
    {
        public FakeEffectRegistry(params VisualEffectDescriptor[] effects) => Effects = effects;
        public IReadOnlyList<VisualEffectDescriptor> Effects { get; }
        public bool TryGet(string effectId, string? version, out VisualEffectDescriptor descriptor)
            => throw new NotSupportedException();
        public void ReplacePackage(string packageId, IEnumerable<VisualEffectDescriptor> effects)
            => throw new NotSupportedException();
        public void RemovePackage(string packageId) => throw new NotSupportedException();
    }

    private sealed class FakePresetReloader : IVisualPresetReloader
    {
        private readonly GeneratorPresetRegistry _registry;
        private readonly GeneratorPreset[] _toRegister;
        public int ReloadCount { get; private set; }

        public FakePresetReloader(GeneratorPresetRegistry registry, params GeneratorPreset[] toRegister)
        {
            _registry = registry;
            _toRegister = toRegister;
        }

        public int Reload()
        {
            ReloadCount++;
            _registry.ReplacePackage("liveolator.frktl.user", _toRegister);
            return _toRegister.Length;
        }
    }

    private static VisualEffectDescriptor Generator(string effectId)
        => new(effectId, "1.0.0", "core", $"{effectId}.frag",
            Array.Empty<VisualEffectParameter>(), VisualEffectRole.Generator);

    public VisualControlViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Theory]
    [InlineData(nameof(VisualControlViewModel.TransitionNowCommand), PerformanceActionKind.VisualTransitionNow)]
    [InlineData(nameof(VisualControlViewModel.TransitionBeatCommand), PerformanceActionKind.VisualTransitionNextBeat)]
    [InlineData(nameof(VisualControlViewModel.TransitionBarCommand), PerformanceActionKind.VisualTransitionNextBar)]
    public async Task TransitionCommand_EmitsVisualAction(string commandName, PerformanceActionKind expected)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new VisualControlViewModel(dispatcher);
        var command = (ReactiveCommand<Unit, Unit>)
            typeof(VisualControlViewModel).GetProperty(commandName)!.GetValue(vm)!;

        await command.Execute().ToTask();

        Assert.Equal(expected, Assert.Single(dispatcher.Dispatched).Kind);
    }

    [Fact]
    public async Task LayerCommand_EmitsSlotAddressedToggle()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new VisualControlViewModel(dispatcher);

        await vm.ToggleLayer3Command.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualToggleLayer, action.Kind);
        Assert.Equal(2, action.Slot);
    }

    [Fact]
    public void Channels_AreDisplayedTopToBottom_AndMapToReverseCompositorSlots()
    {
        var vm = new VisualControlViewModel(new FakeDispatcher());

        Assert.Equal(4, vm.Channels.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, vm.Channels.Select(channel => channel.DisplayOrder));
        Assert.Equal(new[] { 3, 2, 1, 0 }, vm.Channels.Select(channel => channel.LayerSlot));
        Assert.Equal("TOP", vm.Channels[0].DepthLabel);
        Assert.Equal("BOTTOM", vm.Channels[3].DepthLabel);
    }

    [Fact]
    public void SelectingChannelSource_EmitsSerializableLayerSourceAction()
    {
        var dispatcher = new FakeDispatcher();
        var channel = new VisualChannelViewModel(displayOrder: 1, layerSlot: 3, dispatcher);
        var source = new VisualChannelSourceOption(
            "VU Meter",
            "PLUGINS",
            new VisualSourceRef(VisualSourceKind.Generator, "core/vu-meter"));
        channel.ReplaceSources(new[] { source });
        dispatcher.Dispatched.Clear();

        channel.SelectedSource = null;
        channel.SelectedSource = source;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSetLayerSource, action.Kind);
        Assert.Equal(3, action.Slot);
        Assert.True(VisualSourceActionCodec.TryDecode(action.Argument, out VisualSourceRef? decoded));
        Assert.Equal(source.Source, decoded);
    }

    [Fact]
    public void ChannelOpacityKnob_EmitsAbsoluteSetLayerOpacityForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var channel = new VisualChannelViewModel(displayOrder: 1, layerSlot: 3, dispatcher);

        channel.Opacity.Value = 0.4;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSetLayerOpacity, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(3, action.Slot);
        Assert.Equal(0.4, action.Value);
    }

    [Fact]
    public void ChannelOpacityKnob_SyncFromScene_DoesNotReDispatch()
    {
        var dispatcher = new FakeDispatcher();
        var channel = new VisualChannelViewModel(displayOrder: 1, layerSlot: 3, dispatcher);

        channel.SyncOpacityFromScene(0.25);

        Assert.Equal(0.25, channel.Opacity.Value);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public void EveryChannelExposesAnEnabledOpacityKnob_WhenDispatcherWired()
    {
        var vm = new VisualControlViewModel(new FakeDispatcher());

        Assert.All(vm.Channels, channel => Assert.True(channel.Opacity.IsEnabled));
    }

    [Fact]
    public void ChannelSources_LeadWithANoneOption_SoALayerCanBeSwitchedOff()
    {
        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            effectRegistry: new FakeEffectRegistry(Generator("core/vu-meter")));

        foreach (VisualChannelViewModel channel in vm.Channels)
        {
            VisualChannelSourceOption first = channel.Sources[0];
            Assert.Equal("None", first.Label);
            Assert.Equal(VisualSourceKind.None, first.Source.Kind);
        }
    }

    [Fact]
    public void SelectingNone_EmitsADecodableNoneLayerSourceAction()
    {
        var dispatcher = new FakeDispatcher();
        var channel = new VisualChannelViewModel(displayOrder: 1, layerSlot: 3, dispatcher);
        var none = new VisualChannelSourceOption("None", "OFF", VisualSourceRef.None);
        channel.ReplaceSources(new[] { none });
        dispatcher.Dispatched.Clear();

        channel.SelectedSource = null;
        channel.SelectedSource = none;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSetLayerSource, action.Kind);
        Assert.Equal(3, action.Slot);
        Assert.True(VisualSourceActionCodec.TryDecode(action.Argument, out VisualSourceRef? decoded));
        Assert.Equal(VisualSourceKind.None, decoded!.Kind);
    }

    [Fact]
    public async Task ToggleVuMeter_EmitsToggleForConfiguredLayerSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new VisualControlViewModel(dispatcher, vuMeterLayerSlot: 1);

        Assert.True(vm.CanToggleVuMeter);
        await vm.ToggleVuMeterCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualToggleLayer, action.Kind);
        Assert.Equal(1, action.Slot);
    }

    [Fact]
    public void ToggleVuMeter_HiddenWhenNoVuMeterLayerPresent()
    {
        var vm = new VisualControlViewModel(new FakeDispatcher());

        Assert.False(vm.CanToggleVuMeter);
    }

    [Fact]
    public async Task ToggleVuMeter_FlipsShownStateAndButtonLabel()
    {
        var vm = new VisualControlViewModel(
            new FakeDispatcher(), vuMeterLayerSlot: 1, vuMeterInitiallyShown: true);

        Assert.True(vm.IsVuMeterShown);
        Assert.Equal("HIDE VU METER", vm.VuMeterButtonText);

        await vm.ToggleVuMeterCommand.Execute().ToTask();

        Assert.False(vm.IsVuMeterShown);
        Assert.Equal("SHOW VU METER", vm.VuMeterButtonText);
    }

    [Fact]
    public async Task ToggleAddon_UpdatesPackageAndReloadsVisualContent()
    {
        var catalog = new FakeCatalog
        {
            Installed = new[] { CreateVisualAddon("color-pack", enabled: false) },
        };
        var installer = new FakeInstaller();
        var reloader = new FakeReloader();
        var vm = new VisualControlViewModel(
            extensions: catalog,
            extensionInstaller: installer,
            contentReloader: reloader);

        await vm.ToggleAddonCommand.Execute().ToTask();

        Assert.Equal(("color-pack", "1.0.0", true), installer.Toggle);
        Assert.Equal(1, reloader.ReloadCount);
        Assert.Equal(1, catalog.RefreshCount);
    }

    [Fact]
    public async Task ReloadPresetsCommand_ReScansFolderAndRefreshesPicker()
    {
        var presets = new GeneratorPresetRegistry();
        var newPreset = new GeneratorPreset(
            "liveolator.frktl.user/color-pool", "Color Pool",
            "liveolator.frktl.user/generator", "1.0.0",
            new[] { new ControllableParameter("flow", "FLOW") });
        var reloader = new FakePresetReloader(presets, newPreset);
        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            presetRegistry: presets,
            presetReloader: reloader);

        Assert.Empty(vm.PresetControls.Presets);

        await vm.ReloadPresetsCommand.Execute().ToTask();

        Assert.Equal(1, reloader.ReloadCount);
        PresetOptionViewModel option = Assert.Single(vm.PresetControls.Presets);
        Assert.Equal("Color Pool", option.Name);
        Assert.Equal("Reloaded 1 visual preset.", vm.Status);
    }

    [Fact]
    public void ReloadPresetsCommand_DisabledWhenReloaderUnwired()
    {
        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            presetRegistry: new GeneratorPresetRegistry());

        Assert.False(vm.ReloadPresetsCommand.CanExecute.FirstAsync().Wait());
    }

    private static InstalledExtension CreateVisualAddon(string packageId, bool enabled)
    {
        var manifest = new ExtensionManifest(
            packageId,
            "1.0.0",
            "1",
            "Test",
            ExtensionContentKind.VisualEffects,
            Array.Empty<ExtensionDependency>(),
            Array.Empty<ExtensionFile>());
        var validation = new ExtensionValidationResult(
            true,
            manifest,
            null,
            Array.Empty<ExtensionValidationIssue>());
        return new InstalledExtension(manifest, "C:\\test", enabled, DateTimeOffset.UtcNow, validation);
    }
}
