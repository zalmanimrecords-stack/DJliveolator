using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Settings;
using Liveolator.App.Skins;
using Liveolator.App.Theme;
using Liveolator.Core.Audio;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Skins;
using Liveolator.Media.Skins;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Settings;

/// <summary>
/// The control-skin pickers in Settings (doc 30): the knob/slider lists are populated from the catalog
/// filtered by kind, a persisted selection is restored (falling back to built-in when gone), and Save both
/// persists the choice and re-skins the live UI through the applier seam.
/// </summary>
public sealed class SettingsViewModelControlSkinTests
{
    private const string KnobId = "liveolator.control-skins/cobalt-knob";
    private const string SliderId = "liveolator.control-skins/amber-slider";

    public SettingsViewModelControlSkinTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static FakeControlSkinCatalog CatalogWithBoth() => new(
        new LoadedControlSkin(KnobId, new ControlSkinFile { Name = "Cobalt Knob", Kind = ControlSkinKind.Knob, Accent = "#2F80F6" }),
        new LoadedControlSkin(SliderId, new ControlSkinFile { Name = "Amber Slider", Kind = ControlSkinKind.Slider, Accent = "#D78A16" }));

    private static SettingsViewModel NewVm(
        FakeSettingsStore store, IControlSkinCatalog catalog, IControlSkinApplier applier)
        => new(new FakeOutputCatalog(), new FakeCaptureCatalog(), new FakeMidiProvider(), store,
               controlSkins: catalog, controlSkinApplier: applier);

    [Fact]
    public async Task Initialize_PopulatesPickers_FilteredByKind()
    {
        var vm = NewVm(new FakeSettingsStore(), CatalogWithBoth(), new FakeControlSkinApplier());

        await vm.InitializeAsync();

        Assert.Equal(SettingsViewModel.NoSkin, vm.KnobSkinIds[0]);
        Assert.Contains(KnobId, vm.KnobSkinIds);
        Assert.DoesNotContain(SliderId, vm.KnobSkinIds);
        Assert.Contains(SliderId, vm.SliderSkinIds);
        Assert.DoesNotContain(KnobId, vm.SliderSkinIds);
    }

    [Fact]
    public async Task Initialize_RestoresPersistedSelection()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with
            {
                Extensions = new ExtensionSettings { ActiveKnobSkinId = KnobId, ActiveSliderSkinId = SliderId },
            },
        };
        var vm = NewVm(store, CatalogWithBoth(), new FakeControlSkinApplier());

        await vm.InitializeAsync();

        Assert.Equal(KnobId, vm.ActiveKnobSkinId);
        Assert.Equal(SliderId, vm.ActiveSliderSkinId);
    }

    [Fact]
    public async Task Initialize_PersistedSkinGone_FallsBackToBuiltIn()
    {
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with
            {
                Extensions = new ExtensionSettings { ActiveKnobSkinId = "liveolator.control-skins/uninstalled" },
            },
        };
        var vm = NewVm(store, CatalogWithBoth(), new FakeControlSkinApplier());

        await vm.InitializeAsync();

        Assert.Equal(SettingsViewModel.NoSkin, vm.ActiveKnobSkinId);
    }

    [Fact]
    public async Task Save_PersistsSelections_AndAppliesLive()
    {
        var store = new FakeSettingsStore();
        var applier = new FakeControlSkinApplier();
        var vm = NewVm(store, CatalogWithBoth(), applier);
        await vm.InitializeAsync();

        vm.ActiveKnobSkinId = KnobId;
        vm.ActiveSliderSkinId = SliderId;
        await vm.SaveAsync();

        Assert.Equal(KnobId, store.Saved.Extensions.ActiveKnobSkinId);
        Assert.Equal(SliderId, store.Saved.Extensions.ActiveSliderSkinId);
        Assert.True(applier.Called);
        Assert.Equal("Cobalt Knob", applier.LastKnob!.Name);
        Assert.Equal("Amber Slider", applier.LastSlider!.Name);
    }

    [Fact]
    public async Task Save_BuiltIn_PersistsNull_AndAppliesNull()
    {
        var store = new FakeSettingsStore();
        var applier = new FakeControlSkinApplier();
        var vm = NewVm(store, CatalogWithBoth(), applier);
        await vm.InitializeAsync(); // defaults to NoSkin

        await vm.SaveAsync();

        Assert.Null(store.Saved.Extensions.ActiveKnobSkinId);
        Assert.Null(store.Saved.Extensions.ActiveSliderSkinId);
        Assert.True(applier.Called);
        Assert.Null(applier.LastKnob);
        Assert.Null(applier.LastSlider);
    }

    [Fact]
    public void ApplyTheme_resolves_selected_theme_and_applies_it_live()
    {
        var themes = new UiThemeManager();
        themes.ReplacePackage("test", new[] { NeonTheme() });
        var applier = new FakeUiThemeLiveApplier();
        var vm = new SettingsViewModel(
            new FakeOutputCatalog(), new FakeCaptureCatalog(), new FakeMidiProvider(), new FakeSettingsStore(),
            themes: themes, controlSkins: CatalogWithBoth(), controlSkinApplier: new FakeControlSkinApplier(),
            uiThemeLiveApplier: applier);

        vm.ActiveUiThemeId = "Neon";
        vm.ApplyThemeCommand.Execute().Subscribe();

        Assert.Equal("Neon", applier.LastApplied?.Id);
        Assert.Contains("Applied theme 'Neon'", vm.Status);
    }

    // A valid theme definition for the manager (one accent colour is enough to pass validation).
    private static UiThemeDefinition NeonTheme()
        => new("Neon", "Neon", new Dictionary<string, string>(StringComparer.Ordinal) { ["AccentColor"] = "#FF00FF" });

    private sealed class FakeUiThemeLiveApplier : IUiThemeLiveApplier
    {
        public UiThemeDefinition? LastApplied { get; private set; }
        public void Apply(UiThemeDefinition theme) => LastApplied = theme;
    }

    // --- minimal fakes (the rich device fakes live in SettingsViewModelTests; these are just enough) ----

    private sealed class FakeControlSkinCatalog : IControlSkinCatalog
    {
        private readonly List<LoadedControlSkin> _skins;
        public FakeControlSkinCatalog(params LoadedControlSkin[] skins) => _skins = skins.ToList();
        public IReadOnlyList<LoadedControlSkin> Skins => _skins;
        public bool TryGet(string id, out ControlSkinFile skin)
        {
            LoadedControlSkin? match = _skins.FirstOrDefault(s => s.SkinId == id);
            skin = match?.File!;
            return match is not null;
        }
    }

    private sealed class FakeControlSkinApplier : IControlSkinApplier
    {
        public bool Called { get; private set; }
        public ControlSkinFile? LastKnob { get; private set; }
        public ControlSkinFile? LastSlider { get; private set; }
        public void Apply(ControlSkinFile? knob, ControlSkinFile? slider)
        {
            Called = true;
            LastKnob = knob;
            LastSlider = slider;
        }
    }

    private sealed class FakeOutputCatalog : IAudioOutputDeviceCatalog
    {
        public IReadOnlyList<AudioOutputDevice> EnumerateOutputDevices() =>
            new[] { new AudioOutputDevice("1", "Speakers", IsDefault: true) };
    }

    private sealed class FakeCaptureCatalog : IAudioCaptureDeviceCatalog
    {
        public IReadOnlyList<AudioCaptureDevice> EnumerateCaptureDevices() =>
            System.Array.Empty<AudioCaptureDevice>();
    }

    private sealed class FakeMidiProvider : IMidiDeviceProvider
    {
        public IReadOnlyList<string> GetInputDeviceNames() => System.Array.Empty<string>();
        public IReadOnlyList<string> GetOutputDeviceNames() => System.Array.Empty<string>();
        public IMidiInput? OpenInput(string deviceName) => null;
        public IMidiOutput? OpenOutput(string deviceName) => null;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Saved { get; set; } = AppSettings.Default;
        public AppSettings ToLoad { get; set; } = AppSettings.Default;
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(ToLoad);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }
}
