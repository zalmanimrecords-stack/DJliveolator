using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Addons;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Extensions;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Features.Addons;

/// <summary>
/// Verifies the Add-ons tab view-model: it lists the built-in add-ons plus installed packages; selecting
/// the VU meter exposes its background-image settings with the spec-derived guidance; choosing an image
/// persists it (settings store) and applies it live (VisualSetLayerSource at the face slot); the aspect
/// advisory follows the chosen image; and Reset restores the built-in face.
/// </summary>
public sealed class AddonsViewModelTests : IDisposable
{
    private const int FaceSlot = 1;
    private static readonly VuMeterFaceSpec Spec = VuMeterAddon.FaceSpec;

    private readonly string _defaultFace;
    private readonly string _customImage;

    public AddonsViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;

        string dir = Path.Combine(Path.GetTempPath(), "liveolator-addons-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _defaultFace = Path.Combine(dir, "default-face.png");
        _customImage = Path.Combine(dir, "custom-face.png");
        File.WriteAllText(_defaultFace, "default");
        File.WriteAllText(_customImage, "custom");
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_customImage)!, recursive: true); }
        catch (IOException) { /* best-effort cleanup */ }
    }

    private sealed class FakeStore : ISettingsStore
    {
        public AppSettings Saved { get; private set; } = AppSettings.Default;
        public AppSettings ToLoad { get; set; } = AppSettings.Default;
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(ToLoad);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProbe : IImageDimensionsProbe
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Ok { get; set; } = true;
        public bool TryGetPixelSize(string path, out int width, out int height)
        {
            width = Width;
            height = Height;
            return Ok;
        }
    }

    private sealed class FakeCatalog : IExtensionCatalog
    {
        public IReadOnlyList<InstalledExtension> Installed { get; set; } = Array.Empty<InstalledExtension>();
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static InstalledExtension Package(string id, bool enabled)
    {
        var manifest = new ExtensionManifest(
            PackageId: id,
            Version: "1.0.0",
            RequiredApiVersion: "1.0",
            Publisher: "Acme",
            Content: ExtensionContentKind.VisualEffects,
            Dependencies: Array.Empty<ExtensionDependency>(),
            Files: Array.Empty<ExtensionFile>());
        return new InstalledExtension(
            manifest, InstallPath: "/x", IsEnabled: enabled, InstalledAt: DateTimeOffset.UnixEpoch,
            Validation: new ExtensionValidationResult(true, manifest, null, Array.Empty<ExtensionValidationIssue>()));
    }

    private AddonsViewModel Build(
        FakeDispatcher dispatcher,
        FakeStore store,
        int? faceSlot = FaceSlot,
        IExtensionCatalog? catalog = null,
        IImageDimensionsProbe? probe = null,
        string? currentCustom = null)
        => new(dispatcher, store, Spec, _defaultFace, faceSlot, currentCustom, registry: null, catalog, probe);

    [Fact]
    public void Lists_BuiltInsThenInstalledPackages()
    {
        var catalog = new FakeCatalog { Installed = new[] { Package("com.acme.glow", enabled: true) } };

        AddonsViewModel vm = Build(new FakeDispatcher(), new FakeStore(), catalog: catalog);

        Assert.Equal(3, vm.Addons.Count);
        Assert.Equal(VuMeterAddon.EffectId, vm.Addons[0].Id);
        Assert.True(vm.Addons[0].HasSettings);
        Assert.Equal(PsyFractalVisualizerAddon.EffectId, vm.Addons[1].Id);
        Assert.False(vm.Addons[1].HasSettings);
        Assert.Equal("com.acme.glow", vm.Addons[2].Id);
        Assert.False(vm.Addons[2].HasSettings);
        Assert.Equal("Enabled", vm.Addons[2].State);
    }

    [Fact]
    public void SelectsVuMeterByDefault_AndExposesSpecGuidance()
    {
        AddonsViewModel vm = Build(new FakeDispatcher(), new FakeStore());

        Assert.True(vm.ShowVuMeterSettings);
        Assert.False(vm.ShowNoSettingsMessage);
        // The page documents the concrete required size + pivot from the spec.
        Assert.Contains("1200", vm.VuMeterSettings.SizeRequirement);
        Assert.Contains("800", vm.VuMeterSettings.SizeRequirement);
        Assert.Contains("600", vm.VuMeterSettings.PivotRequirement);
        Assert.Contains("576", vm.VuMeterSettings.PivotRequirement);
    }

    [Fact]
    public void SelectingNonConfigurableAddon_ShowsNoSettingsMessage()
    {
        AddonsViewModel vm = Build(new FakeDispatcher(), new FakeStore());

        vm.SelectedAddon = vm.Addons.First(a => a.Id == PsyFractalVisualizerAddon.EffectId);

        Assert.False(vm.ShowVuMeterSettings);
        Assert.True(vm.ShowNoSettingsMessage);
    }

    [Fact]
    public async Task ChooseImage_PersistsAndAppliesLiveAtFaceSlot()
    {
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore();
        AddonsViewModel vm = Build(dispatcher, store);

        await vm.VuMeterSettings.ChooseImageAsync(_customImage);

        Assert.Equal(_customImage, store.Saved.Addons.VuMeterBackgroundImagePath);
        Assert.True(vm.VuMeterSettings.IsCustom);
        Assert.Equal(_customImage, vm.VuMeterSettings.ImagePath);

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSetLayerSource, action.Kind);
        Assert.Equal(FaceSlot, action.Slot);
        Assert.True(VisualSourceActionCodec.TryDecode(action.Argument, out VisualSourceRef? source));
        Assert.Equal(VisualSourceKind.Image, source!.Kind);
        Assert.Equal(_customImage, source.Reference);
    }

    [Fact]
    public async Task ChooseImage_MissingFile_DoesNotPersistOrDispatch()
    {
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore();
        AddonsViewModel vm = Build(dispatcher, store);

        await vm.VuMeterSettings.ChooseImageAsync(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.png"));

        Assert.Empty(dispatcher.Dispatched);
        Assert.Null(store.Saved.Addons.VuMeterBackgroundImagePath);
        Assert.False(vm.VuMeterSettings.IsCustom);
    }

    [Fact]
    public async Task ChooseImage_WithoutFaceSlot_PersistsButDoesNotDispatch()
    {
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore();
        AddonsViewModel vm = Build(dispatcher, store, faceSlot: null);

        await vm.VuMeterSettings.ChooseImageAsync(_customImage);

        Assert.Equal(_customImage, store.Saved.Addons.VuMeterBackgroundImagePath);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public async Task ChooseImage_NonMatchingAspect_WarnsAndStandardAspectDoesNot()
    {
        var probe = new FakeProbe { Width = 1000, Height = 1000 }; // 1:1, not 3:2
        AddonsViewModel vm = Build(new FakeDispatcher(), new FakeStore(), probe: probe);

        await vm.VuMeterSettings.ChooseImageAsync(_customImage);
        Assert.False(string.IsNullOrEmpty(vm.VuMeterSettings.AspectWarning));

        probe.Width = 1200;
        probe.Height = 800; // exactly 3:2
        await vm.VuMeterSettings.ChooseImageAsync(_customImage);
        Assert.Null(vm.VuMeterSettings.AspectWarning);
    }

    [Fact]
    public async Task ResetToDefault_ClearsPathAndAppliesDefaultFace()
    {
        var dispatcher = new FakeDispatcher();
        var store = new FakeStore();
        AddonsViewModel vm = Build(dispatcher, store, currentCustom: _customImage);
        Assert.True(vm.VuMeterSettings.IsCustom);

        await vm.VuMeterSettings.ResetToDefaultCommand.Execute().ToTask();

        Assert.Null(store.Saved.Addons.VuMeterBackgroundImagePath);
        Assert.False(vm.VuMeterSettings.IsCustom);
        Assert.Equal(_defaultFace, vm.VuMeterSettings.ImagePath);
        Assert.Null(vm.VuMeterSettings.AspectWarning);

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.True(VisualSourceActionCodec.TryDecode(action.Argument, out VisualSourceRef? source));
        Assert.Equal(_defaultFace, source!.Reference);
    }
}
