using System;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Stems;
using ReactiveUI;

namespace Liveolator.App.Features.Dj;

/// <summary>
/// The DJ PRO per-deck STEM rack: one volume knob per stem (DRUMS / BASS / VOCALS / OTHER), each driving
/// the deck's 4-stem submix through <see cref="PerformanceActionKind.DeckStemGain"/> (the doc 04 seam).
/// Knobs seed to unity (full level) and are ENABLED only while the loaded track is a 4-stem deck — tracked
/// from <see cref="PerformanceActionKind.DeckStemGain"/> feedback (which carries
/// <c>IsAvailable = IsStemDeck</c>), so a normal single-file track shows the knobs greyed rather than as
/// inert controls that silently do nothing. A null dispatcher (headless / no realtime engine) leaves the
/// rack permanently unavailable.
/// </summary>
public sealed class DeckStemRackViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly int _slot;
    private bool _isAvailable;
    private bool _disposed;

    /// <param name="dispatcher">The one action layer; null disables the rack.</param>
    /// <param name="slot">The deck slot the rack belongs to (A = 0, B = 1).</param>
    public DeckStemRackViewModel(IPerformanceActionDispatcher? dispatcher, int slot)
    {
        _dispatcher = dispatcher;
        _slot = slot;

        ContinuousControlViewModel Knob(StemKind kind) => new(
            kind.ToString().ToUpperInvariant(),
            1.0, // unity — full stem level
            dispatcher is null
                ? null
                : value => dispatcher.Dispatch(new PerformanceAction(
                    PerformanceActionKind.DeckStemGain, ActionInputMode.Absolute,
                    Value: value, Slot: slot, Argument: kind.ToString())));

        Drums = Knob(StemKind.Drums);
        Bass = Knob(StemKind.Bass);
        Vocals = Knob(StemKind.Vocals);
        Other = Knob(StemKind.Other);

        // Enabled only for a stem deck: seed from current feedback (false when nothing / a single file is
        // loaded), then track it live. The dispatcher marshals FeedbackChanged to the UI thread.
        _isAvailable = dispatcher?.GetFeedback(PerformanceActionKind.DeckStemGain, slot).IsAvailable ?? false;
        if (dispatcher is not null)
            dispatcher.FeedbackChanged += OnFeedback;
    }

    /// <summary>Drums stem level (1 = full).</summary>
    public ContinuousControlViewModel Drums { get; }

    /// <summary>Bass stem level.</summary>
    public ContinuousControlViewModel Bass { get; }

    /// <summary>Vocals stem level.</summary>
    public ContinuousControlViewModel Vocals { get; }

    /// <summary>Other (melody/harmony) stem level.</summary>
    public ContinuousControlViewModel Other { get; }

    /// <summary>True only while the loaded track is a 4-stem deck; the view disables the knobs otherwise.</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        private set => this.RaiseAndSetIfChanged(ref _isAvailable, value);
    }

    // All four stems carry the same availability (the whole deck is a stem deck or not), so any DeckStemGain
    // echo for this slot updates the rack-level gate. A load relights all four (RaiseStemFeedback).
    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Kind == PerformanceActionKind.DeckStemGain && e.Slot == _slot)
            IsAvailable = e.State.IsAvailable;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_dispatcher is not null)
            _dispatcher.FeedbackChanged -= OnFeedback;
    }
}
