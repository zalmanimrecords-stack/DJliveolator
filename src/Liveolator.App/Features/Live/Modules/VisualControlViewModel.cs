using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Extensions;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Visuals;
using Liveolator.Core.Visuals.TrackPrograms;
using Liveolator.Media.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// Live visual control surface. Performance operations use the action dispatcher; extension package
/// lifecycle stays on the extension service because it changes the available visual vocabulary.
/// </summary>
public sealed class VisualControlViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IExtensionCatalog? _extensions;
    private readonly IExtensionInstaller? _extensionInstaller;
    private readonly IExtensionContentReloader? _contentReloader;
    private readonly IVisualEffectRegistry? _effectRegistry;
    private readonly IVisualPerformanceEngine? _visualEngine;
    private readonly ILivePlaylist? _playlist;
    private readonly ITrackVisualProgramStore? _trackVisualPrograms;
    private readonly IVisualPresetReloader? _presetReloader;
    private readonly IGeneratorPresetRegistry? _presetRegistry;
    // The starter scene's VU-meter generator layer index (resolved at composition); null hides the
    // VU-meter toggle when the active scene ships no built-in meter. The toggle addresses this slot
    // via VisualToggleLayer, mirroring the generic layer buttons.
    private readonly int? _vuMeterLayerSlot;
    private VisualAddonViewModel? _selectedAddon;
    private bool _isVuMeterShown;
    private int _launchQuantizeMode;
    private string _status = "Ready";

    public VisualControlViewModel(
        IPerformanceActionDispatcher? dispatcher = null,
        IVisualStage? visualStage = null,
        IVisualEffectRegistry? effectRegistry = null,
        IExtensionCatalog? extensions = null,
        IExtensionInstaller? extensionInstaller = null,
        IExtensionContentReloader? contentReloader = null,
        int? vuMeterLayerSlot = null,
        bool vuMeterInitiallyShown = true,
        IVisualPerformanceEngine? visualEngine = null,
        ILivePlaylist? playlist = null,
        ITrackVisualProgramStore? trackVisualPrograms = null,
        IGeneratorPresetRegistry? presetRegistry = null,
        IVisualPresetReloader? presetReloader = null)
    {
        _dispatcher = dispatcher;
        _extensions = extensions;
        _extensionInstaller = extensionInstaller;
        _contentReloader = contentReloader;
        _effectRegistry = effectRegistry;
        _visualEngine = visualEngine;
        _playlist = playlist;
        _trackVisualPrograms = trackVisualPrograms;
        _presetReloader = presetReloader;
        _presetRegistry = presetRegistry;
        _vuMeterLayerSlot = vuMeterLayerSlot;
        _isVuMeterShown = vuMeterInitiallyShown;

        ShowVisualsCommand = ReactiveCommand.Create(
            () => visualStage?.Show(),
            Observable.Return(visualStage is not null));

        ToggleVuMeterCommand = ReactiveCommand.Create(
            ToggleVuMeter,
            Observable.Return(dispatcher is not null && vuMeterLayerSlot is not null));

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
                .Select(addon => addon is not null && addon.CanToggle && extensionInstaller is not null));

        // Each channel owns its layer's controllable-preset knobs (doc 28): selecting a preset generator in
        // a channel's source dropdown loads it onto that layer and shows its ≤5 knobs in the same card,
        // driven through the same VisualSetMacro path as the macro encoders and a learned MIDI knob.
        Channels = new ObservableCollection<VisualChannelViewModel>(
            Enumerable.Range(0, 4)
                .Select(row => new VisualChannelViewModel(
                    displayOrder: row + 1,
                    layerSlot: 3 - row,
                    dispatcher,
                    presetRegistry,
                    effectRegistry)));

        // Re-scan the FRKTL preset folder at runtime (doc 29) so presets authored while the app is running
        // (e.g. via the MCP server) appear in the layer source dropdowns without a restart. Needs the
        // reloader + the registry the channels read from to be wired.
        ReloadPresetsCommand = ReactiveCommand.CreateFromTask(
            ReloadPresetsAsync,
            Observable.Return(presetReloader is not null && presetRegistry is not null));

        // Off/Beat/Bar quantize for scene-pad launches (doc 31): publishing the mode through the dispatcher
        // (VisualSetLaunchQuantize, Value = 0/1/2) so the visual handler snaps launches to the shared clock.
        // Skip the initial value — the handler already defaults to off, so only a user change emits.
        this.WhenAnyValue(x => x.LaunchQuantizeMode)
            .Skip(1)
            .Subscribe(mode => _dispatcher?.Dispatch(
                new PerformanceAction(PerformanceActionKind.VisualSetLaunchQuantize, Value: mode)));

        ReloadEffects();
        ReloadAddons();
        ReloadChannelSourcesAsync(_playlist?.Now?.TrackPath).GetAwaiter().GetResult();
        if (_playlist is not null)
            _playlist.NowChanged += OnNowChanged;
    }

    public ReactiveCommand<Unit, Unit> ShowVisualsCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleVuMeterCommand { get; }
    public ReactiveCommand<Unit, Unit> TransitionNowCommand { get; }
    public ReactiveCommand<Unit, Unit> TransitionBeatCommand { get; }
    public ReactiveCommand<Unit, Unit> TransitionBarCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleLayer1Command { get; }
    public ReactiveCommand<Unit, Unit> ToggleLayer2Command { get; }
    public ReactiveCommand<Unit, Unit> ToggleLayer3Command { get; }
    public ReactiveCommand<Unit, Unit> ToggleLayer4Command { get; }
    public ReactiveCommand<Unit, Unit> ToggleAddonCommand { get; }

    /// <summary>Re-scans the user FRKTL preset folder and refreshes the picker (doc 29). Disabled when unwired.</summary>
    public ReactiveCommand<Unit, Unit> ReloadPresetsCommand { get; }

    /// <summary>Scene-launch quantize options for the Off/Beat/Bar selector (index = the dispatched mode).</summary>
    public IReadOnlyList<string> LaunchQuantizeOptions { get; } = new[] { "Off", "Beat", "Bar" };

    /// <summary>Selected scene-launch quantize mode: 0 = off (immediate), 1 = next beat, 2 = next bar
    /// (doc 31). Changing it publishes <see cref="PerformanceActionKind.VisualSetLaunchQuantize"/>.</summary>
    public int LaunchQuantizeMode
    {
        get => _launchQuantizeMode;
        set => this.RaiseAndSetIfChanged(ref _launchQuantizeMode, value);
    }

    public ObservableCollection<string> LoadedEffects { get; } = new();
    public ObservableCollection<VisualChannelViewModel> Channels { get; }
    public ObservableCollection<VisualAddonViewModel> Addons { get; } = new();
    public bool HasAddons => Addons.Count > 0;

    /// <summary>True when the active scene ships a built-in VU-meter layer to show/hide.</summary>
    public bool CanToggleVuMeter => _vuMeterLayerSlot is not null;

    /// <summary>Tracks whether the VU-meter layer is currently shown, so the button label reflects state.</summary>
    public bool IsVuMeterShown
    {
        get => _isVuMeterShown;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isVuMeterShown, value);
            this.RaisePropertyChanged(nameof(VuMeterButtonText));
        }
    }

    public string VuMeterButtonText => _isVuMeterShown ? "HIDE VU METER" : "SHOW VU METER";

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

    // Show/hide the built-in VU meter by toggling its layer (VisualToggleLayer flips the layer opacity
    // 0↔1). Local IsVuMeterShown mirrors the engine state so the button label stays in sync; it is
    // seeded from the active scene at composition and only diverges if the scene is swapped underneath.
    private void ToggleVuMeter()
    {
        if (_vuMeterLayerSlot is not int slot)
            return;
        _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.VisualToggleLayer, Slot: slot));
        IsVuMeterShown = !IsVuMeterShown;
    }

    private ReactiveCommand<Unit, Unit> CreateLayerCommand(int layer, IObservable<bool> canExecute)
        => ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.VisualToggleLayer,
                Slot: layer)),
            canExecute);

    private async Task ToggleSelectedAddonAsync()
    {
        VisualAddonViewModel? selected = SelectedAddon;
        if (selected is null || !selected.CanToggle || _extensionInstaller is null)
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
            await ReloadChannelSourcesAsync(_playlist?.Now?.TrackPath);
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
            Addons.Add(VisualAddonViewModel.ForExtension(extension));

        // User FRKTL presets (doc 29) are listed flat alongside the extension add-ons so the operator sees
        // the whole visual vocabulary in one place. They live as .frktl files and are always active, so
        // they appear for visibility but are not toggleable (CanToggle = false).
        foreach (GeneratorPreset preset in (_presetRegistry?.Presets ?? Array.Empty<GeneratorPreset>())
                     .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
            Addons.Add(VisualAddonViewModel.ForFrktlPreset(preset.Name));

        SelectedAddon = Addons.FirstOrDefault(addon =>
            string.Equals(addon.PackageId, selectedId, StringComparison.Ordinal)
            && string.Equals(addon.Version, selectedVersion, StringComparison.Ordinal))
            ?? Addons.FirstOrDefault();
        this.RaisePropertyChanged(nameof(HasAddons));
    }

    // Re-scan the preset folder, then refresh the layer source dropdowns so newly-registered generators
    // appear. The reload is tolerant by contract (never throws); the guard + Status keep any unexpected IO
    // surprise visible rather than silent (global standards #16/#26).
    private async Task ReloadPresetsAsync()
    {
        if (_presetReloader is null)
            return;

        try
        {
            int count = _presetReloader.Reload();
            ReloadEffects();
            ReloadAddons();
            await ReloadChannelSourcesAsync(_playlist?.Now?.TrackPath);
            // Re-apply the reloaded preset to every layer that has one, so its knobs + the running
            // shader pick up the new parameter set with no manual reselect (the old stale-knob trap).
            foreach (VisualChannelViewModel channel in Channels)
                channel.ReapplyPresetIfLoaded();
            Status = $"Reloaded {count} visual preset{(count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Preset reload failed: {ex.Message}";
        }
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

    public void Dispose()
    {
        foreach (VisualChannelViewModel channel in Channels)
            channel.Dispose();
        if (_playlist is not null)
            _playlist.NowChanged -= OnNowChanged;
    }

    private async void OnNowChanged(object? sender, QueueEntry? now)
        => await ReloadChannelSourcesAsync(now?.TrackPath);

    private async Task ReloadChannelSourcesAsync(string? trackPath)
    {
        List<VisualChannelSourceOption> options = BuildGeneratorSourceOptions();

        if (!string.IsNullOrWhiteSpace(trackPath) && _trackVisualPrograms is not null)
        {
            TrackVisualProgram? program = await _trackVisualPrograms.LoadAsync(trackPath);
            options.AddRange((program?.Cues ?? Array.Empty<TrackVisualCue>())
                .Where(cue => cue.Asset.Kind == VisualMediaKind.Image)
                .Select(cue => new VisualChannelSourceOption(
                    Path.GetFileNameWithoutExtension(cue.Asset.Path),
                    "TRACK IMAGES",
                    new VisualSourceRef(VisualSourceKind.Image, cue.Asset.Path)))
                .DistinctBy(option => option.Source.Reference, StringComparer.OrdinalIgnoreCase));
        }

        VisualScene? scene = _visualEngine?.ActiveBank.Scene(0);
        if (scene is not null)
            AppendMissingSceneGenerators(options, scene);

        foreach (VisualChannelViewModel channel in Channels)
        {
            VisualLayer? layer = scene is not null && channel.LayerSlot < scene.Layers.Count
                ? scene.Layers[channel.LayerSlot]
                : null;
            channel.ReplaceSources(options, layer?.Source);
            if (layer is not null)
                channel.SyncOpacityFromScene(layer.Opacity);
        }
    }

    /// <summary>
    /// Builds the generator/preset entries for the layer source picker (doc 29). Controllable presets are
    /// listed by their authored <see cref="GeneratorPreset.Name"/>; plain generators without a preset
    /// wrapper follow under PLUGINS.
    /// </summary>
    private List<VisualChannelSourceOption> BuildGeneratorSourceOptions()
    {
        // "None" leads the list so a layer can be switched off from the UI without leaving the scene.
        var options = new List<VisualChannelSourceOption>
        {
            new("None", "OFF", VisualSourceRef.None),
        };

        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameByEffectId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (GeneratorPreset preset in _presetRegistry?.Presets ?? Array.Empty<GeneratorPreset>())
            nameByEffectId[preset.GeneratorEffectId] = preset.Name;

        foreach (GeneratorPreset preset in (_presetRegistry?.Presets ?? Array.Empty<GeneratorPreset>())
                     .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!listed.Add(preset.GeneratorEffectId))
                continue;

            bool loaded = TryResolveGenerator(preset.GeneratorEffectId, preset.GeneratorVersion, out _);
            options.Add(new VisualChannelSourceOption(
                loaded ? preset.Name : $"{preset.Name} (not loaded)",
                "PRESETS",
                new VisualSourceRef(VisualSourceKind.Generator, preset.GeneratorEffectId)));
        }

        // User-authored folder presets can register as effects even when the preset row is out of sync;
        // always surface the liveolator.frktl.user package in PRESETS so the picker matches doc 29.
        foreach (VisualEffectDescriptor effect in (_effectRegistry?.Effects ?? Array.Empty<VisualEffectDescriptor>())
                     .Where(effect =>
                         effect.Role == VisualEffectRole.Generator
                         && string.Equals(effect.PackageId, FrktlPresetFolderLoader.PackageId, StringComparison.Ordinal))
                     .OrderBy(effect => effect.EffectId, StringComparer.OrdinalIgnoreCase))
        {
            if (!listed.Add(effect.EffectId))
                continue;

            string label = nameByEffectId.TryGetValue(effect.EffectId, out string? presetName)
                ? presetName
                : VisualSourceLabel.Humanize(effect.EffectId);
            options.Add(new VisualChannelSourceOption(
                label,
                "PRESETS",
                new VisualSourceRef(VisualSourceKind.Generator, effect.EffectId)));
        }

        foreach (VisualEffectDescriptor effect in (_effectRegistry?.Effects ?? Array.Empty<VisualEffectDescriptor>())
                     .Where(effect => effect.Role == VisualEffectRole.Generator)
                     .OrderBy(effect => effect.EffectId, StringComparer.OrdinalIgnoreCase))
        {
            if (listed.Contains(effect.EffectId))
                continue;

            options.Add(new VisualChannelSourceOption(
                VisualSourceLabel.Humanize(effect.EffectId),
                "PLUGINS",
                new VisualSourceRef(VisualSourceKind.Generator, effect.EffectId)));
        }

        return options;
    }

    private bool TryResolveGenerator(
        string effectId,
        string? version,
        out VisualEffectDescriptor descriptor)
    {
        descriptor = default!;
        if (_effectRegistry is null)
            return false;

        if (_effectRegistry.TryGet(effectId, version, out descriptor) && descriptor.Role == VisualEffectRole.Generator)
            return true;

        if (_effectRegistry.TryGet(effectId, null, out descriptor) && descriptor.Role == VisualEffectRole.Generator)
            return true;

        descriptor = default!;
        return false;
    }

    /// <summary>
    /// Keeps scene-referenced generators visible in the picker when their preset file is missing or was
    /// skipped at load time, so the operator sees what the saved scene expects instead of a silent fallback
    /// to None.
    /// </summary>
    private static void AppendMissingSceneGenerators(List<VisualChannelSourceOption> options, VisualScene scene)
    {
        var known = new HashSet<string>(
            options.Where(option => option.Source.Kind == VisualSourceKind.Generator)
                .Select(option => option.Source.Reference),
            StringComparer.OrdinalIgnoreCase);

        foreach (VisualLayer layer in scene.Layers)
        {
            if (layer.Source.Kind != VisualSourceKind.Generator
                || string.IsNullOrWhiteSpace(layer.Source.Reference)
                || known.Contains(layer.Source.Reference))
                continue;

            options.Add(new VisualChannelSourceOption(
                $"{VisualSourceLabel.Humanize(layer.Source.Reference)} (not loaded)",
                "MISSING",
                layer.Source));
            known.Add(layer.Source.Reference);
        }
    }
}
