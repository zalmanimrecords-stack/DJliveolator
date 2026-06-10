using System;
using System.Collections.ObjectModel;
using System.Reactive;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Extensions;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using ReactiveUI;

namespace Liveolator.App.Features.Addons;

/// <summary>
/// The Add-ons tab (doc 26): one place that lists every add-on — the built-in visual generators plus any
/// installed extension packages — each with a Settings button. Today only the built-in VU meter is
/// configurable (swap its dial-face/background image while the needle stays standard); other add-ons are
/// listed read-only. UI-free and unit-testable with fakes — it emits <c>PerformanceAction</c>s through
/// the dispatcher and persists via <see cref="ISettingsStore"/>, never calling an engine directly (doc 04).
/// </summary>
public sealed class AddonsViewModel : ViewModelBase
{
    private readonly VuMeterBackgroundSettingsViewModel _vuMeterSettings;
    private AddonItemViewModel? _selectedAddon;

    public AddonsViewModel(
        IPerformanceActionDispatcher dispatcher,
        ISettingsStore store,
        VuMeterFaceSpec vuMeterFaceSpec,
        string defaultVuMeterFacePath,
        int? vuMeterFaceLayerSlot,
        string? currentVuMeterCustomFacePath = null,
        IVisualEffectRegistry? registry = null,
        IExtensionCatalog? extensions = null,
        IImageDimensionsProbe? imageProbe = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(vuMeterFaceSpec);

        _vuMeterSettings = new VuMeterBackgroundSettingsViewModel(
            dispatcher, store, vuMeterFaceLayerSlot, defaultVuMeterFacePath, vuMeterFaceSpec,
            currentVuMeterCustomFacePath, imageProbe);

        OpenSettingsCommand = ReactiveCommand.Create<AddonItemViewModel>(item => SelectedAddon = item);

        BuildAddonList(registry, extensions);
        SelectedAddon = Addons.Count > 0 ? Addons[0] : null;
    }

    /// <summary>Every add-on, built-ins first, then installed packages.</summary>
    public ObservableCollection<AddonItemViewModel> Addons { get; } = new();

    /// <summary>The settings panel for the built-in VU meter (shown when its row is selected).</summary>
    public VuMeterBackgroundSettingsViewModel VuMeterSettings => _vuMeterSettings;

    public AddonItemViewModel? SelectedAddon
    {
        get => _selectedAddon;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAddon, value);
            this.RaisePropertyChanged(nameof(ShowVuMeterSettings));
            this.RaisePropertyChanged(nameof(ShowNoSettingsMessage));
        }
    }

    /// <summary>True when the selected add-on is the VU meter — reveal its background-image panel.</summary>
    public bool ShowVuMeterSettings =>
        string.Equals(SelectedAddon?.Id, VuMeterAddon.EffectId, StringComparison.Ordinal);

    /// <summary>True when an add-on is selected but has no configurable settings yet.</summary>
    public bool ShowNoSettingsMessage => SelectedAddon is { HasSettings: false };

    /// <summary>Selects an add-on (the per-row "Settings" button binds here).</summary>
    public ReactiveCommand<AddonItemViewModel, Unit> OpenSettingsCommand { get; }

    private void BuildAddonList(IVisualEffectRegistry? registry, IExtensionCatalog? extensions)
    {
        Addons.Clear();

        // Built-in visual add-ons. The VU meter is the configurable reference add-on (doc 26).
        Addons.Add(new AddonItemViewModel(
            id: VuMeterAddon.EffectId,
            title: "VU Meter",
            description: "Analog VU meter — swap the dial-face background image; the needle stays standard.",
            hasSettings: true,
            isBuiltIn: true,
            state: BuiltInState(registry, VuMeterAddon.EffectId)));

        Addons.Add(new AddonItemViewModel(
            id: PsyFractalVisualizerAddon.EffectId,
            title: "Psy Fractal Visualizer",
            description: "Audio-reactive fractal mandala generator. No configurable settings yet.",
            hasSettings: false,
            isBuiltIn: true,
            state: BuiltInState(registry, PsyFractalVisualizerAddon.EffectId)));

        // Installed extension packages (the same packages managed under Settings → EXTENSIONS).
        if (extensions is not null)
        {
            foreach (InstalledExtension extension in extensions.Installed)
            {
                Addons.Add(new AddonItemViewModel(
                    id: extension.Manifest.PackageId,
                    title: extension.Manifest.PackageId,
                    description: $"{extension.Manifest.Publisher} · {extension.Manifest.Content}",
                    hasSettings: false,
                    isBuiltIn: false,
                    state: extension.IsEnabled ? "Enabled" : "Disabled"));
            }
        }
    }

    // "Built-in" when loaded, "Unavailable" when the registry is present but the generator failed to
    // register (e.g. an asset write error at startup). A null registry (tests) reads as built-in.
    private static string BuiltInState(IVisualEffectRegistry? registry, string effectId)
    {
        if (registry is null)
            return "Built-in";
        return registry.TryGet(effectId, version: null, out _) ? "Built-in" : "Unavailable";
    }
}
