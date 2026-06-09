using System.Collections.ObjectModel;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>One top-to-bottom visual channel row. UI order is the inverse of compositor order.</summary>
public sealed class VisualChannelViewModel : ViewModelBase
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private VisualChannelSourceOption? _selectedSource;
    private bool _suppressDispatch;

    public VisualChannelViewModel(int displayOrder, int layerSlot, IPerformanceActionDispatcher? dispatcher)
    {
        DisplayOrder = displayOrder;
        LayerSlot = layerSlot;
        _dispatcher = dispatcher;
    }

    public int DisplayOrder { get; }
    public int LayerSlot { get; }
    public string LayerLabel => $"LAYER {DisplayOrder}";
    public string DepthLabel => DisplayOrder == 1 ? "TOP" : DisplayOrder == 4 ? "BOTTOM" : string.Empty;
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

            _dispatcher?.Dispatch(new PerformanceAction(
                PerformanceActionKind.VisualSetLayerSource,
                Slot: LayerSlot,
                Argument: VisualSourceActionCodec.Encode(value.Source)));
        }
    }

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
}
