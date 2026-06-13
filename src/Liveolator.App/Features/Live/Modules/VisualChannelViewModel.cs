using System.Collections.ObjectModel;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>One top-to-bottom visual channel row. UI order is the inverse of compositor order.</summary>
public sealed class VisualChannelViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private VisualChannelSourceOption? _selectedSource;
    private bool _suppressDispatch;

    public VisualChannelViewModel(
        int displayOrder,
        int layerSlot,
        IPerformanceActionDispatcher? dispatcher,
        IGeneratorPresetRegistry? presets = null,
        IVisualEffectRegistry? effects = null)
    {
        DisplayOrder = displayOrder;
        LayerSlot = layerSlot;
        _dispatcher = dispatcher;
        // Per-layer opacity knob. Emits an Absolute VisualSetLayerOpacity for this channel's compositor
        // slot (doc 04 seam); a null dispatcher disables the knob. Starts at full (1.0) like a fresh layer.
        Opacity = new ContinuousControlViewModel(
            "OPACITY",
            initial: 1.0,
            dispatcher is not null ? DispatchOpacity : null);
        // The preset knob surface for THIS layer (doc 28): when the source dropdown selects a generator
        // backed by a controllable preset, the preset loads onto this slot and its knobs appear here.
        Preset = new PresetControlsViewModel(presets, effects, dispatcher, targetLayer: layerSlot);
    }

    public int DisplayOrder { get; }
    public int LayerSlot { get; }
    public string LayerLabel => $"LAYER {DisplayOrder}";
    public string DepthLabel => DisplayOrder == 1 ? "TOP" : DisplayOrder == 4 ? "BOTTOM" : string.Empty;

    /// <summary>This layer's opacity control (0..1), driving <c>VisualSetLayerOpacity</c> for its slot.</summary>
    public ContinuousControlViewModel Opacity { get; }

    /// <summary>This layer's controllable-preset knobs, shown when the selected source is a preset generator.</summary>
    public PresetControlsViewModel Preset { get; }

    public ObservableCollection<VisualChannelSourceOption> Sources { get; } = new();

    public VisualChannelSourceOption? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (Equals(_selectedSource, value))
                return;
            this.RaiseAndSetIfChanged(ref _selectedSource, value);
            if (value is null || _suppressDispatch)
                return;

            // A generator backed by a controllable preset loads via the preset path, which both places the
            // generator on this layer AND installs its knobs (doc 28); anything else (image / None / a
            // generator with no preset) just sets the layer source and clears the knob row.
            if (value.Source.Kind == VisualSourceKind.Generator
                && Preset.TryLoadForGeneratorSource(value.Source.Reference))
                return;

            Preset.ClearControls();
            _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.VisualSetLayerSource,
                Slot: LayerSlot,
                Argument: VisualSourceActionCodec.Encode(value.Source)));
        }
    }

    // Reflects a layer's current opacity in the knob WITHOUT re-dispatching (SetFromFeedback bypasses the
    // emit path) — used when (re)loading the scene so the knob position matches the engine state.
    public void SyncOpacityFromScene(double opacity) => Opacity.SetFromFeedback(opacity);

    private void DispatchOpacity(double value)
        => _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.VisualSetLayerOpacity,
            ActionInputMode.Absolute,
            Value: value,
            Slot: LayerSlot));

    public void ReplaceSources(
        IEnumerable<VisualChannelSourceOption> sources,
        VisualSourceRef? preferredSource = null)
    {
        string? previousKind = SelectedSource?.Source.Kind.ToString();
        string? previousReference = SelectedSource?.Source.Reference;

        _suppressDispatch = true;
        try
        {
            Sources.Clear();
            foreach (VisualChannelSourceOption source in sources)
                Sources.Add(source);

            SelectedSource = Find(preferredSource?.Kind.ToString(), preferredSource?.Reference)
                ?? Find(previousKind, previousReference)
                ?? Sources.FirstOrDefault();
        }
        finally
        {
            _suppressDispatch = false;
        }
    }

    private VisualChannelSourceOption? Find(string? kind, string? reference)
        => Sources.FirstOrDefault(option =>
            string.Equals(option.Source.Kind.ToString(), kind, StringComparison.Ordinal)
            && string.Equals(option.Source.Reference, reference, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => Preset.Dispose();
}
