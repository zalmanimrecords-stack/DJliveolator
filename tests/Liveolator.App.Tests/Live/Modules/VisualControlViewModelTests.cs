using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Extensions;
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
