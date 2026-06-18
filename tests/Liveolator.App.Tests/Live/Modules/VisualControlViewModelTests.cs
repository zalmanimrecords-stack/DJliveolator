using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.IO;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Extensions;
using Liveolator.Core.Visuals;
using Liveolator.Media.Visuals;
using Liveolator.Visuals.Gl;
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
        private readonly VisualEffectRegistry _effects;
        public int ReloadCount { get; private set; }

        public FakePresetReloader(VisualEffectRegistry effects) => _effects = effects;

        public int Reload()
        {
            ReloadCount++;
            _effects.ReplacePackage("liveolator.frktl.user", new[]
            {
                new VisualEffectDescriptor(
                    "liveolator.frktl.user/color-pool", "1.0.0", "liveolator.frktl.user", "color-pool.frag",
                    Array.Empty<VisualEffectParameter>(), VisualEffectRole.Generator),
            });
            return 1;
        }
    }

    private sealed class FakeVisualEngineWithScene : IVisualPerformanceEngine
    {
        public FakeVisualEngineWithScene(VisualScene scene)
            => ActiveBank = new VisualBank("Live", new[] { scene });

        public VisualBank ActiveBank { get; }
        public IReadOnlyList<string> BankNames { get; } = new[] { "Live" };

        public void SelectBank(int index) => throw new NotSupportedException();
        public void LoadScene(VisualScene scene, Quantize when, int everyN = 1) => throw new NotSupportedException();
        public void LoadPreset(GeneratorPresetBinding binding, int layer, Quantize when, int everyN = 1)
            => throw new NotSupportedException();
        public void SetMacro(string name, double value) => throw new NotSupportedException();
        public void SetLayerSource(int layer, VisualSourceRef source, Quantize when, int everyN = 1)
            => throw new NotSupportedException();
        public void ToggleLayer(int layer) => throw new NotSupportedException();
        public void SetLayerOpacity(int layer, double opacity) => throw new NotSupportedException();
        public void LaunchClip(int layer, string clipId, Quantize when, int everyN = 1) => throw new NotSupportedException();
        public void Blackout(bool on) => throw new NotSupportedException();
        public void Strobe(bool on) => throw new NotSupportedException();
        public void Transition(TransitionStyle style, Quantize when, int everyN = 1) => throw new NotSupportedException();
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
    public void ChannelSources_ListPresetsByAuthoredName_WhenPresetRegistryWired()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = PresetRegistries();
        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            effectRegistry: effects,
            presetRegistry: presets);

        VisualChannelSourceOption presetOption = Assert.Single(
            vm.Channels[0].Sources,
            source => source.Group == "PRESETS");
        Assert.Equal("Preset", presetOption.Label);
        Assert.Equal("pkg/gen", presetOption.Source.Reference);
    }

    [Fact]
    public void ChannelSources_ShowMissingSceneGenerator_WhenPresetFileIsNotLoaded()
    {
        var effects = new VisualEffectRegistry();
        PsyFractalVisualizerAddon.TryRegister(effects);
        var engine = new FakeVisualEngineWithScene(new VisualScene(
            "Live",
            new[]
            {
                new VisualLayer(
                    "Star",
                    new VisualSourceRef(VisualSourceKind.Generator, "liveolator.frktl.user/star-of-david"),
                    Array.Empty<EffectRef>(),
                    BlendMode.Normal,
                    1.0),
            },
            new Dictionary<string, double>(),
            TransitionStyle.Cut,
            BeatBehavior.None));

        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            effectRegistry: effects,
            visualEngine: engine);

        VisualChannelSourceOption missing = Assert.Single(
            vm.Channels[0].Sources,
            source => source.Group == "MISSING");
        Assert.Equal("Star Of David (not loaded)", missing.Label);
    }

    [Fact]
    public void ChannelSources_ListUserFrktlPackageUnderPresetsGroup()
    {
        var effects = new VisualEffectRegistry();
        var presets = new GeneratorPresetRegistry();
        string folder = Path.Combine(Path.GetTempPath(), "liveolator-frktl-vm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string shader =
                "#version 330 core\nin vec2 vTexCoord;\nout vec4 fragColor;\nuniform float uGlow;\nvoid main(){ fragColor = vec4(uGlow); }";
            string json = $$"""
                {
                  "name": "COLOR POOL",
                  "parameters": [
                    { "id": "glow", "uniform": "uGlow", "label": "GLOW", "min": 0.0, "max": 2.0, "default": 1.0 }
                  ],
                  "shader": {{System.Text.Json.JsonSerializer.Serialize(shader)}}
                }
                """;
            File.WriteAllText(Path.Combine(folder, "color-pool.frktl"), json);
            new FrktlPresetFolderLoader(effects, presets, folder).Load();

            var vm = new VisualControlViewModel(
                new FakeDispatcher(),
                effectRegistry: effects,
                presetRegistry: presets);

            VisualChannelSourceOption option = Assert.Single(
                vm.Channels[0].Sources,
                source => source.Label == "COLOR POOL");
            Assert.Equal("PRESETS", option.Group);
            Assert.Equal("liveolator.frktl.user/color-pool", option.Source.Reference);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
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
    public async Task ReloadPresetsCommand_ReScansFolder_AndRefreshesLayerSources()
    {
        var effects = new VisualEffectRegistry();
        var reloader = new FakePresetReloader(effects);
        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            effectRegistry: effects,
            presetRegistry: new GeneratorPresetRegistry(),
            presetReloader: reloader);

        Assert.DoesNotContain(vm.Channels[0].Sources, s => s.Label == "Color Pool");

        await vm.ReloadPresetsCommand.Execute().ToTask();

        Assert.Equal(1, reloader.ReloadCount);
        Assert.Contains(vm.Channels[0].Sources, s => s.Label == "Color Pool");
        Assert.Equal("Reloaded 1 visual preset.", vm.Status);
    }

    [Fact]
    public void Addons_ListUserFrktlPresets_FlatAlongsideExtensions_AsNonToggleableEntries()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = PresetRegistries();
        var catalog = new FakeCatalog { Installed = new[] { CreateVisualAddon("color-pack", enabled: true) } };
        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            effectRegistry: effects,
            extensions: catalog,
            presetRegistry: presets);

        Assert.True(vm.HasAddons);
        // The extension package is toggleable; the FRKTL preset is listed but not.
        Assert.Contains(vm.Addons, a => a.PackageId == "color-pack" && a.CanToggle);
        VisualAddonViewModel presetRow = Assert.Single(vm.Addons, a => a.PackageId == "Preset");
        Assert.Equal("FRKTL", presetRow.State);
        Assert.False(presetRow.CanToggle);
    }

    [Fact]
    public void ToggleAddonCommand_IsDisabled_WhenAFrktlPresetIsSelected()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = PresetRegistries();
        // The only add-on is the FRKTL preset (auto-selected); the installer is wired, but a preset is
        // always-active and must not be toggleable from here.
        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            effectRegistry: effects,
            extensionInstaller: new FakeInstaller(),
            presetRegistry: presets);

        Assert.NotNull(vm.SelectedAddon);
        Assert.False(vm.SelectedAddon!.CanToggle);
        Assert.False(vm.ToggleAddonCommand.CanExecute.FirstAsync().Wait());
    }

    [Fact]
    public void ReloadPresetsCommand_DisabledWhenReloaderUnwired()
    {
        var vm = new VisualControlViewModel(
            new FakeDispatcher(),
            presetRegistry: new GeneratorPresetRegistry());

        Assert.False(vm.ReloadPresetsCommand.CanExecute.FirstAsync().Wait());
    }

    private static (VisualEffectRegistry Effects, GeneratorPresetRegistry Presets) PresetRegistries()
    {
        var effects = new VisualEffectRegistry();
        effects.ReplacePackage("pkg", new[]
        {
            new VisualEffectDescriptor("pkg/gen", "1.0.0", "pkg", "gen.frag",
                new[] { new VisualEffectParameter("glow", "uGlow", 0, 1, 0.5) }, VisualEffectRole.Generator),
        });
        var presets = new GeneratorPresetRegistry();
        presets.ReplacePackage("pkg", new[]
        {
            new GeneratorPreset("pkg/preset", "Preset", "pkg/gen", "1.0.0",
                new[] { new ControllableParameter("glow", "GLOW") }),
        });
        return (effects, presets);
    }

    [Fact]
    public void SelectingPresetGeneratorSource_LoadsPresetOntoLayer_AndShowsItsKnobs()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = PresetRegistries();
        var dispatcher = new FakeDispatcher();
        var channel = new VisualChannelViewModel(displayOrder: 1, layerSlot: 3, dispatcher, presets, effects);
        var genOption = new VisualChannelSourceOption(
            "Gen", "PLUGINS", new VisualSourceRef(VisualSourceKind.Generator, "pkg/gen"));
        channel.ReplaceSources(new[] { genOption });
        dispatcher.Dispatched.Clear();

        channel.SelectedSource = null;
        channel.SelectedSource = genOption;

        // A preset-backed generator loads via the preset path (places generator + installs knobs) — NOT a
        // bare VisualSetLayerSource.
        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualLoadPreset, action.Kind);
        Assert.Equal(3, action.Slot);
        Assert.Equal("pkg/preset", action.Argument);
        Assert.True(channel.Preset.HasControls);
        Assert.Equal(new[] { "GLOW" }, channel.Preset.Controls.Select(c => c.Label));
    }

    [Fact]
    public void ReapplyPresetIfLoaded_RebuildsKnobs_ForAPresetLayer_WithoutReselect()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = PresetRegistries();
        var dispatcher = new FakeDispatcher();
        var channel = new VisualChannelViewModel(displayOrder: 1, layerSlot: 3, dispatcher, presets, effects);
        var genOption = new VisualChannelSourceOption(
            "Gen", "PLUGINS", new VisualSourceRef(VisualSourceKind.Generator, "pkg/gen"));
        // ReplaceSources restores the selection silently (suppressed) -> no knobs yet, like after a reload.
        channel.ReplaceSources(new[] { genOption }, new VisualSourceRef(VisualSourceKind.Generator, "pkg/gen"));
        Assert.False(channel.Preset.HasControls);
        dispatcher.Dispatched.Clear();

        channel.ReapplyPresetIfLoaded();

        // The preset is loaded onto the layer and its knobs rebuilt, no manual reselect required.
        Assert.True(channel.Preset.HasControls);
        Assert.Contains(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.VisualLoadPreset && a.Slot == 3);
    }

    [Fact]
    public void SelectingNonPresetGeneratorSource_SetsLayerSource_WithNoKnobs()
    {
        (VisualEffectRegistry effects, GeneratorPresetRegistry presets) = PresetRegistries();
        var dispatcher = new FakeDispatcher();
        var channel = new VisualChannelViewModel(displayOrder: 1, layerSlot: 3, dispatcher, presets, effects);
        var plainGen = new VisualChannelSourceOption(
            "Plain", "PLUGINS", new VisualSourceRef(VisualSourceKind.Generator, "pkg/no-preset"));
        channel.ReplaceSources(new[] { plainGen });
        dispatcher.Dispatched.Clear();

        channel.SelectedSource = null;
        channel.SelectedSource = plainGen;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSetLayerSource, action.Kind);
        Assert.Equal(3, action.Slot);
        Assert.False(channel.Preset.HasControls);
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
