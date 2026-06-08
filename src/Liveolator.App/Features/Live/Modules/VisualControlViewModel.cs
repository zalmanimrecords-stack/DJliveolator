using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Extensions;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// Live visual control surface. Performance operations use the action dispatcher; extension package
/// lifecycle stays on the extension service because it changes the available visual vocabulary.
/// </summary>
public sealed class VisualControlViewModel : ViewModelBase
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IExtensionCatalog? _extensions;
    private readonly IExtensionInstaller? _extensionInstaller;
    private readonly IExtensionContentReloader? _contentReloader;
    private readonly IVisualEffectRegistry? _effectRegistry;
    private VisualAddonViewModel? _selectedAddon;
    private string _status = "Ready";

    public VisualControlViewModel(
        IPerformanceActionDispatcher? dispatcher = null,
        IVisualStage? visualStage = null,
        IVisualEffectRegistry? effectRegistry = null,
        IExtensionCatalog? extensions = null,
        IExtensionInstaller? extensionInstaller = null,
        IExtensionContentReloader? contentReloader = null)
    {
        _dispatcher = dispatcher;
        _extensions = extensions;
        _extensionInstaller = extensionInstaller;
        _contentReloader = contentReloader;
        _effectRegistry = effectRegistry;

        ShowVisualsCommand = ReactiveCommand.Create(
            () => visualStage?.Show(),
            Observable.Return(visualStage is not null));

        IObservable<bool> canEmit = Observable.Return(dispatcher is not null);
        TransitionNowCommand = CreateActionCommand(PerformanceActionKind.VisualTransitionNow, canEmit);
        TransitionBeatCommand = CreateActionCommand(PerformanceActionKind.VisualTransitionNextBeat, canEmit);
        TransitionBarCommand = CreateActionCommand(PerformanceActionKind.VisualTransitionNextBar, canEmit);
        ToggleLayer1Command = CreateLayerCommand(0, canEmit);
        ToggleLayer2Command = CreateLayerCommand(1, canEmit);
        ToggleLayer3Command = CreateLayerCommand(2, canEmit);
        ToggleLayer4Command = CreateLayerCommand(3, canEmit);

        ToggleAddonCommand = ReactiveCommand.CreateFromTask(
            ToggleSelectedAddonAsync,
            this.WhenAnyValue(vm => vm.SelectedAddon)
                .Select(addon => addon is not null && extensionInstaller is not null));

        ReloadEffects();
        ReloadAddons();
    }

    public ReactiveCommand<Unit, Unit> ShowVisualsCommand { get; }
    public ReactiveCommand<Unit, Unit> TransitionNowCommand { get; }
    public ReactiveCommand<Unit, Unit> TransitionBeatCommand { get; }
    public ReactiveCommand<Unit, Unit> TransitionBarCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleLayer1Command { get; }
    public ReactiveCommand<Unit, Unit> ToggleLayer2Command { get; }
    public ReactiveCommand<Unit, Unit> ToggleLayer3Command { get; }
    public ReactiveCommand<Unit, Unit> ToggleLayer4Command { get; }
    public ReactiveCommand<Unit, Unit> ToggleAddonCommand { get; }

    public ObservableCollection<string> LoadedEffects { get; } = new();
    public ObservableCollection<VisualAddonViewModel> Addons { get; } = new();
    public bool HasAddons => Addons.Count > 0;

    public VisualAddonViewModel? SelectedAddon
    {
        get => _selectedAddon;
        set => this.RaiseAndSetIfChanged(ref _selectedAddon, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    private ReactiveCommand<Unit, Unit> CreateActionCommand(
        PerformanceActionKind kind,
        IObservable<bool> canExecute)
        => ReactiveCommand.Create(() => _dispatcher?.Dispatch(new PerformanceAction(kind)), canExecute);

    private ReactiveCommand<Unit, Unit> CreateLayerCommand(int layer, IObservable<bool> canExecute)
        => ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.VisualToggleLayer,
                Slot: layer)),
            canExecute);

    private async Task ToggleSelectedAddonAsync()
    {
        VisualAddonViewModel? selected = SelectedAddon;
        if (selected is null || _extensionInstaller is null)
            return;

        try
        {
            await _extensionInstaller.SetEnabledAsync(
                selected.PackageId,
                selected.Version,
                !selected.IsEnabled);
            if (_contentReloader is not null)
                await _contentReloader.ReloadAsync();
            if (_extensions is not null)
                await _extensions.RefreshAsync();

            ReloadEffects();
            ReloadAddons();
            Status = $"{selected.PackageId} is now {(selected.IsEnabled ? "disabled" : "enabled")}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            Status = $"Add-on update failed: {ex.Message}";
        }
    }

    private void ReloadAddons()
    {
        string? selectedId = SelectedAddon?.PackageId;
        string? selectedVersion = SelectedAddon?.Version;
        Addons.Clear();

        IEnumerable<InstalledExtension> visualAddons = (_extensions?.Installed ?? Array.Empty<InstalledExtension>())
            .Where(extension =>
                extension.Manifest.Content.HasFlag(ExtensionContentKind.VisualEffects)
                || extension.Manifest.Content.HasFlag(ExtensionContentKind.VisualShow))
            .OrderBy(extension => extension.Manifest.PackageId, StringComparer.OrdinalIgnoreCase);

        foreach (InstalledExtension extension in visualAddons)
            Addons.Add(new VisualAddonViewModel(extension));

        SelectedAddon = Addons.FirstOrDefault(addon =>
            string.Equals(addon.PackageId, selectedId, StringComparison.Ordinal)
            && string.Equals(addon.Version, selectedVersion, StringComparison.Ordinal))
            ?? Addons.FirstOrDefault();
        this.RaisePropertyChanged(nameof(HasAddons));
    }

    private void ReloadEffects()
    {
        LoadedEffects.Clear();
        IEnumerable<string> names = (_effectRegistry?.Effects ?? Array.Empty<VisualEffectDescriptor>())
            .Select(effect => $"{effect.EffectId} ({effect.Role})")
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
            LoadedEffects.Add(name);
    }
}
