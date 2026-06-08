using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;

namespace Liveolator.App.Composition;

/// <summary>Restores deck loads through the action seam and autosaves later load feedback.</summary>
internal sealed class DeckSessionPersistence : IDisposable
{
    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly IDeckSessionStore _store;
    private readonly Dictionary<int, DeckSessionState> _decks = new();
    private readonly object _gate = new();
    private Task _pendingSave = Task.CompletedTask;
    private bool _disposed;

    public DeckSessionPersistence(
        IPerformanceActionDispatcher dispatcher,
        IDeckSessionStore store,
        int deckCount)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        Restore(deckCount);
        _dispatcher.FeedbackChanged += OnFeedbackChanged;
    }

    private void Restore(int deckCount)
    {
        try
        {
            IReadOnlyList<DeckSessionState>? saved = _store.LoadAsync().GetAwaiter().GetResult();
            if (saved is null)
                return;

            foreach (DeckSessionState deck in saved)
            {
                if (deck.Slot < 0 || deck.Slot >= deckCount || !File.Exists(deck.TrackPath))
                    continue;

                _decks[deck.Slot] = deck;
                _dispatcher.Dispatch(new PerformanceAction(
                    PerformanceActionKind.DeckLoadTrack,
                    ActionInputMode.Absolute,
                    Value: deck.Bpm,
                    Slot: deck.Slot,
                    Argument: deck.TrackPath));
                _dispatcher.Dispatch(new PerformanceAction(
                    PerformanceActionKind.DeckSetFirstBeat,
                    ActionInputMode.Absolute,
                    Value: deck.FirstBeatSeconds,
                    Slot: deck.Slot));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not restore the deck session: {ex.Message}.");
        }
    }

    private void OnFeedbackChanged(object? sender, ActionFeedbackChanged e)
    {
        lock (_gate)
        {
            if (e.Kind == PerformanceActionKind.DeckLoadTrack
                && e.State.IsAvailable
                && !string.IsNullOrWhiteSpace(e.State.Argument))
            {
                _decks[e.Slot] = new DeckSessionState(
                    e.Slot, e.State.Argument!, e.State.Value, FirstBeatSeconds: 0);
                QueueSaveLocked();
            }
            else if (e.Kind == PerformanceActionKind.DeckSetFirstBeat
                     && _decks.TryGetValue(e.Slot, out DeckSessionState? deck))
            {
                _decks[e.Slot] = deck with { FirstBeatSeconds = e.State.Value };
                QueueSaveLocked();
            }
        }
    }

    private void QueueSaveLocked()
    {
        DeckSessionState[] snapshot = _decks.Values.OrderBy(deck => deck.Slot).ToArray();
        _pendingSave = _pendingSave.ContinueWith(
            _ => _store.SaveAsync(snapshot),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
        _ = _pendingSave.ContinueWith(
            task => System.Diagnostics.Trace.TraceWarning(
                $"Deck session could not be saved: {task.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _dispatcher.FeedbackChanged -= OnFeedbackChanged;
        try
        {
            _pendingSave.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Deck session could not finish saving during shutdown: {ex.Message}.");
        }
        _disposed = true;
    }
}
