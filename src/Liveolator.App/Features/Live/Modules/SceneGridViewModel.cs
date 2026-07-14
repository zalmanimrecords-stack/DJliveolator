using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// The Visual Scene Grid module (doc 12 Module 4 / the mock's scene pads): an 8×8 grid mirroring the
/// Push pad layout plus a bank switcher. Pads emit <see cref="PerformanceActionKind.VisualLoadScene"/>
/// and the bank tabs emit <see cref="PerformanceActionKind.VisualSelectBank"/> through the dispatcher
/// (doc 04). Pad state is seeded and updated from dispatcher feedback so the grid is a pure on-screen
/// mirror of the engine/pads (doc 12, one source of truth).
/// </summary>
public sealed class SceneGridViewModel : ViewModelBase, IDisposable
{
    /// <summary>Columns / rows / total cells of the grid (the Push 8×8 pad layout).</summary>
    public const int Columns = 8;
    public const int Rows = 8;
    public const int PadCount = Columns * Rows;

    // Fallback bank labels (the mock's phases) used only when no real banks are supplied — e.g. headless
    // construction or tests. The live app passes the engine's actual bank names (doc 22 C3), so a tab
    // selecting bank index N maps VisualSelectBank to the real bank N.
    private static readonly string[] FallbackBankNames = { "Warmup", "Peak", "Breaks", "Outro" };

    private readonly IPerformanceActionDispatcher? _dispatcher;
    private int _selectedBankIndex;
    private bool _disposed;

    /// <param name="dispatcher">Action layer; null disables the grid (no engine wired).</param>
    /// <param name="bankNames">The real bank names from the engine, in selection-index order; null/empty
    /// falls back to the mock's phase labels so the grid still renders headless / under test.</param>
    public SceneGridViewModel(
        IPerformanceActionDispatcher? dispatcher = null,
        IReadOnlyList<string>? bankNames = null)
    {
        _dispatcher = dispatcher;

        Pads = new ObservableCollection<ScenePadViewModel>(
            Enumerable.Range(0, PadCount).Select(slot => new ScenePadViewModel(slot, dispatcher)));
        Banks = new ObservableCollection<string>(
            bankNames is { Count: > 0 } ? bankNames : FallbackBankNames);

        if (_dispatcher is not null)
        {
            foreach (ScenePadViewModel pad in Pads)
                pad.Apply(_dispatcher.GetFeedback(PerformanceActionKind.VisualLoadScene, pad.Slot));
            _dispatcher.FeedbackChanged += OnFeedback;
        }
    }

    /// <summary>True when the visual handler is wired; the UI disables the grid otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null;

    /// <summary>The 64 scene pads, row-major.</summary>
    public ObservableCollection<ScenePadViewModel> Pads { get; }

    /// <summary>The bank tab labels.</summary>
    public ObservableCollection<string> Banks { get; }

    /// <summary>The selected bank tab; setting it emits a <see cref="PerformanceActionKind.VisualSelectBank"/>.</summary>
    public int SelectedBankIndex
    {
        get => _selectedBankIndex;
        set
        {
            int previous = _selectedBankIndex;
            this.RaiseAndSetIfChanged(ref _selectedBankIndex, value);
            if (_selectedBankIndex != previous && _dispatcher is not null && value >= 0)
                _dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.VisualSelectBank, Slot: value));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Kind != PerformanceActionKind.VisualLoadScene || e.Slot < 0 || e.Slot >= Pads.Count)
            return;
        RxApp.MainThreadScheduler.Schedule(() => Pads[e.Slot].Apply(e.State));
    }
}
