using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Addons;
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
/// the VU meter exposes its settings (needle origin + background image) with the spec-derived AI prompt;
/// choosing an image / origin persists it and applies it live via the injected callback; the aspect
/// advisory follows the chosen image; and Reset restores the built-in face.
/// </summary>
public sealed class AddonsViewModelTests : IDisposable
{
    private readonly string _defaultFace;
    private readonly string _customImage;
    private readonly List<(string? Path, VuMeterNeedleOrigin Origin)> _applied = new();

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
            PackageId: id, Version: "1.0.0", RequiredApiVersion: "1.0", Publisher: "Acme",
            Content: ExtensionContentKind.VisualEffects,
            Dependencies: Array.Empty<ExtensionDependency>(), Files: Array.Empty<ExtensionFile>());
        return new InstalledExtension(
            manifest, InstallPath: "/x", IsEnabled: enabled, InstalledAt: DateTimeOffset.UnixEpoch,
            Validation: new ExtensionValidationResult(true, manifest, null, Array.Empty<ExtensionValidationIssue>()));
    }

    private AddonsViewModel Build(
        FakeStore store,
        IExtensionCatalog? catalog = null,
        IImageDimensionsProbe? probe = null,
        string? currentCustom = null,
        VuMeterNeedleOrigin origin = VuMeterNeedleOrigin.Bottom)
        => new(
            store, VuMeterAddon.FaceSpec, _ => _defaultFace, currentCustom, origin,
            (p, o) => _applied.Add((p, o)), registry: null, catalog, probe);

    [Fact]
    public void Lists_BuiltInsThenInstalledPackages()
    {
        var catalog = new FakeCatalog { Installed = new[] { Package("com.acme.glow", enabled: true) } };

        AddonsViewModel vm = Build(new FakeStore(), catalog: catalog);

        Assert.Equal(3, vm.Addons.Count);
        Assert.Equal(VuMeterAddon.EffectId, vm.Addons[0].Id);
        Assert.True(vm.Addons[0].HasSettings);
        Assert.Equal(PsyFractalVisualizerAddon.EffectId, vm.Addons[1].Id);
        Assert.False(vm.Addons[1].HasSettings);
        Assert.Equal("com.acme.glow", vm.Addons[2].Id);
    }

    [Fact]
    public void SelectsVuMeterByDefault_AndPromptMatchesBottomOrigin()
    {
        AddonsViewModel vm = Build(new FakeStore());

        Assert.True(vm.ShowVuMeterSettings);
        Assert.Equal(VuMeterNeedleOrigin.Bottom, vm.VuMeterSettings.SelectedOrigin);
        string prompt = vm.VuMeterSettings.ImagePrompt;
        Assert.Contains("1200", prompt);
        Assert.Contains("800", prompt);
        Assert.Contains("624", prompt); // Bottom hub pixel Y (0.78 * 800)
        Assert.Contains("needle", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChooseImage_PersistsAndAppliesBackgroundLive()
    {
        var store = new FakeStore();
        AddonsViewModel vm = Build(store);

        await vm.VuMeterSettings.ChooseImageAsync(_customImage);

        Assert.Equal(_customImage, store.Saved.Addons.VuMeterBackgroundImagePath);
        Assert.Equal((_customImage, VuMeterNeedleOrigin.Bottom), Assert.Single(_applied));
        Assert.True(vm.VuMeterSettings.IsCustom);
        Assert.Equal(_customImage, vm.VuMeterSettings.ImagePath);
    }

    [Fact]
    public async Task ChooseImage_MissingFile_DoesNotPersistOrApply()
    {
        var store = new FakeStore();
        AddonsViewModel vm = Build(store);

        await vm.VuMeterSettings.ChooseImageAsync(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.png"));

        Assert.Empty(_applied);
        Assert.Null(store.Saved.Addons.VuMeterBackgroundImagePath);
        Assert.False(vm.VuMeterSettings.IsCustom);
    }

    [Fact]
    public async Task ChooseImage_NonMatchingAspect_WarnsAndStandardAspectDoesNot()
    {
        var probe = new FakeProbe { Width = 1000, Height = 1000 }; // 1:1, not 3:2
        AddonsViewModel vm = Build(new FakeStore(), probe: probe);

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
        var store = new FakeStore();
        AddonsViewModel vm = Build(store, currentCustom: _customImage);
        Assert.True(vm.VuMeterSettings.IsCustom);

        await vm.VuMeterSettings.ResetToDefaultCommand.Execute().ToTask();

        Assert.Null(store.Saved.Addons.VuMeterBackgroundImagePath);
        Assert.Equal((null, VuMeterNeedleOrigin.Bottom), Assert.Single(_applied));
        Assert.False(vm.VuMeterSettings.IsCustom);
        Assert.Equal(_defaultFace, vm.VuMeterSettings.ImagePath);
    }

    [Fact]
    public void ChangingOrigin_PersistsAppliesAndUpdatesPrompt()
    {
        var store = new FakeStore();
        AddonsViewModel vm = Build(store);

        vm.VuMeterSettings.SelectedOrigin = VuMeterNeedleOrigin.Top;

        Assert.Equal(VuMeterNeedleOrigin.Top, store.Saved.Addons.VuMeterNeedleOrigin);
        Assert.Equal((null, VuMeterNeedleOrigin.Top), Assert.Single(_applied));
        // The prompt now describes the TOP origin (hub high — 0.22 * 800 = 176 px).
        Assert.Contains("176", vm.VuMeterSettings.ImagePrompt);
    }
}
