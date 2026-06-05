using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// A single DJ deck (the mock's Deck A / Deck B, doc 11), parameterized by slot (A = 0, B = 1).
/// Wired now: Play·Pause (<see cref="PerformanceActionKind.DeckPlayPause"/>), the 3-band EQ
/// (<see cref="PerformanceActionKind.MixerEqBand"/>) and the single-knob filter
/// (<see cref="PerformanceActionKind.MixerFilter"/>) — all through the dispatcher (doc 04).
/// Cue, Loop, Sync, the four hot-cues and the pitch fader have no Core handler yet (doc 18) and are
/// exposed as disabled controls so the deck matches the mock without emitting dead actions.
/// </summary>
public sealed class DeckViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly int _slot;
    private bool _isPlaying;
    private bool _disposed;

    public DeckViewModel(int slot, IPerformanceActionDispatcher? dispatcher = null)
    {
        _slot = slot;
        _dispatcher = dispatcher;
        DeckId = slot == 0 ? "A" : "B";
        bool enabled = dispatcher is not null;

        PlayPauseCommand = ReactiveCommand.Create(
            () => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: slot)),
            Observable.Return(enabled));

        EqHigh = new ContinuousControlViewModel("Hi", EqBands_Unity, enabled ? v => EmitEq("High", v) : null);
        EqMid = new ContinuousControlViewModel("Mid", EqBands_Unity, enabled ? v => EmitEq("Mid", v) : null);
        EqLow = new ContinuousControlViewModel("Low", EqBands_Unity, enabled ? v => EmitEq("Low", v) : null);
        Filter = new ContinuousControlViewModel(
            "Flt", Seed(PerformanceActionKind.MixerFilter, FilterCentre),
            enabled ? v => Emit(PerformanceActionKind.MixerFilter, v) : null);

        // Disabled-but-labeled: no Core handler yet (doc 18). A null callback disables the control.
        Pitch = new ContinuousControlViewModel("Pitch", PitchCentre, onUserChanged: null);

        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged += OnFeedback;
    }

    // EqBands.Unity (0.5) = flat; MixerMath maps 0..1 to boost/cut. Filter/pitch centre likewise.
    private const double EqBands_Unity = 0.5;
    private const double FilterCentre = 0.5;
    private const double PitchCentre = 0.5;

    /// <summary>Deck label, "A" or "B".</summary>
    public string DeckId { get; }

    /// <summary>Placeholder title until a track-load action sets a real one (no track is loaded at startup).</summary>
    public string Title => "No track loaded";

    /// <summary>Placeholder deck meta line (key · pitch · time) until a track is loaded.</summary>
    public string Meta => "—";

    /// <summary>True when transport/EQ can be driven; the UI disables those controls otherwise.</summary>
    public bool IsEnabled => _dispatcher is not null;

    /// <summary>True while this deck is playing (drives the Play key's active state), from dispatcher feedback.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set => this.RaiseAndSetIfChanged(ref _isPlaying, value);
    }

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ContinuousControlViewModel EqHigh { get; }
    public ContinuousControlViewModel EqMid { get; }
    public ContinuousControlViewModel EqLow { get; }
    public ContinuousControlViewModel Filter { get; }
    public ContinuousControlViewModel Pitch { get; }

    /// <summary>Cue/Loop/Sync/Hot-cues have no handler yet — surfaced disabled (doc 18).</summary>
    public bool CanCue => false;
    public bool CanLoop => false;
    public bool CanSync => false;
    public bool CanHotCue => false;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }

    private double Seed(PerformanceActionKind kind, double fallback)
    {
        ActionFeedbackState? feedback = _dispatcher?.GetFeedback(kind, _slot);
        return feedback is { IsAvailable: true } ? feedback.Value : fallback;
    }

    private void EmitEq(string band, double value)
        => _dispatcher?.Dispatch(new PerformanceAction(
            PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, Value: value, Slot: _slot, Argument: band));

    private void Emit(PerformanceActionKind kind, double value)
        => _dispatcher?.Dispatch(new PerformanceAction(kind, ActionInputMode.Absolute, Value: value, Slot: _slot));

    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Slot != _slot)
            return;
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            switch (e.Kind)
            {
                case PerformanceActionKind.MixerFilter:
                    Filter.SetFromFeedback(e.State.Value);
                    break;
                case PerformanceActionKind.DeckPlayPause:
                    IsPlaying = e.State.IsActive;
                    break;
            }
        });
    }
}
