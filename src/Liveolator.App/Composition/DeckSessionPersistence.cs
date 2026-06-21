using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.App.Composition;

/// <summary>
/// Restores the tracks loaded on the performance decks across restarts (through the action seam) and
/// autosaves later load feedback. Two failure modes this guards against:
/// <list type="bullet">
///   <item>An offline drive at launch (e.g. the network music share, Unavailable until mounted): the
///   saved deck entry is <em>kept</em>, not dropped, and its load is <em>deferred</em> rather than fed to
///   the engine as a doomed BASS open (which the engine cannot tell apart from a real one — see
///   <see cref="Liveolator.Core.Playlist.DeckTrackLoader"/>). A retry loads it the moment the path
///   becomes reachable.</item>
///   <item>A second startup loader re-loading the same deck with no analysis (BPM/first-beat = 0):
///   the previously-restored anchor is preserved instead of being clobbered to zero.</item>
/// </list>
/// </summary>
internal sealed class DeckSessionPersistence : IDisposable
{
    // How often to re-check a deferred (offline) deck track for reachability. A few seconds is well
    // below any human "restart and start mixing" window, and a missed mount just waits one more tick.
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly IDeckSessionStore _store;
    private readonly Func<string, bool> _fileExists;
    private readonly ILogger _logger;
    private readonly Dictionary<int, DeckSessionState> _decks = new();
    // Decks whose file was offline at restore, awaiting the drive to mount (keyed by slot).
    private readonly Dictionary<int, DeckSessionState> _pending = new();
    private readonly object _gate = new();
    private readonly Timer? _retryTimer;
    private Task _pendingSave = Task.CompletedTask;
    private bool _disposed;

    /// <param name="fileExists">File-reachability probe (the composition root passes <c>File.Exists</c>;
    /// injected so the offline/deferred path stays unit-testable).</param>
    /// <param name="logger">Writes restore/defer diagnostics to the rolling log file; null = no logging.</param>
    /// <param name="enableRetryTimer">Arms the background reachability retry (false in unit tests, which
    /// drive <see cref="RetryPending"/> deterministically).</param>
    public DeckSessionPersistence(
        IPerformanceActionDispatcher dispatcher,
        IDeckSessionStore store,
        int deckCount,
        Func<string, bool>? fileExists = null,
        ILogger<DeckSessionPersistence>? logger = null,
        bool enableRetryTimer = true)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _fileExists = fileExists ?? File.Exists;
        _logger = logger ?? (ILogger)NullLogger<DeckSessionPersistence>.Instance;

        Restore(deckCount);
        // Subscribe AFTER restoring so the restore's own load dispatches don't echo back and re-save.
        _dispatcher.FeedbackChanged += OnFeedbackChanged;

        // Only run the reachability poll when something was actually deferred — an all-local session
        // never starts a timer.
        bool hasPending;
        lock (_gate)
            hasPending = _pending.Count > 0;
        if (enableRetryTimer && hasPending)
            _retryTimer = new Timer(_ => RetryPending(), null, RetryInterval, RetryInterval);
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
                if (deck.Slot < 0 || deck.Slot >= deckCount)
                    continue;

                // Keep the entry regardless of reachability so an offline track is never lost from the
                // saved session — it stays in _decks and is re-saved on the next change.
                _decks[deck.Slot] = deck;

                if (_fileExists(deck.TrackPath))
                {
                    DispatchLoad(deck);
                }
                else
                {
                    // Drive/share offline at launch. Do NOT dispatch a doomed load — the engine cannot
                    // distinguish a failed BASS open from a real one (DeckTrackLoader's invariant), and the
                    // load would throw before any feedback reached the deck UI. Defer and auto-load on mount.
                    _pending[deck.Slot] = deck;
                    _logger.LogInformation(
                        "Deck {Slot} track is offline at launch; deferring load until reachable: {Path}",
                        deck.Slot, deck.TrackPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restore the deck session.");
        }
    }

    // Re-checks deferred (offline) decks and loads any whose path has become reachable, with the saved
    // BPM + first-beat anchor. Internal so unit tests drive it deterministically; the timer calls it.
    internal void RetryPending()
    {
        if (_disposed)
            return;

        DeckSessionState[] snapshot;
        lock (_gate)
        {
            if (_pending.Count == 0)
                return;
            snapshot = _pending.Values.ToArray();
        }

        // Probe reachability and dispatch OUTSIDE the lock — File.Exists on a network path can block,
        // and the dispatch synchronously echoes feedback into OnFeedbackChanged (which takes _gate).
        var loaded = new List<int>();
        foreach (DeckSessionState deck in snapshot)
        {
            if (!_fileExists(deck.TrackPath))
                continue;
            DispatchLoad(deck);
            _logger.LogInformation(
                "Deferred deck {Slot} track became reachable; loaded {Path}.", deck.Slot, deck.TrackPath);
            loaded.Add(deck.Slot);
        }

        if (loaded.Count == 0)
            return;

        lock (_gate)
        {
            foreach (int slot in loaded)
                _pending.Remove(slot);
            // Stop polling once every deferred track has loaded.
            if (_pending.Count == 0)
                _retryTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void DispatchLoad(DeckSessionState deck)
    {
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
        // Re-apply a manually-set downbeat ("one") so a grid edit survives the restart. Only when one was
        // saved (> 0) — a 0 would just echo the default and the deck would keep its auto-resolved downbeat.
        if (deck.DownbeatSeconds > 0)
            _dispatcher.Dispatch(new PerformanceAction(
                PerformanceActionKind.DeckSetDownbeat,
                ActionInputMode.Absolute,
                Value: deck.DownbeatSeconds,
                Slot: deck.Slot));
    }

    private void OnFeedbackChanged(object? sender, ActionFeedbackChanged e)
    {
        lock (_gate)
        {
            if (e.Kind == PerformanceActionKind.DeckLoadTrack
                && e.State.IsAvailable
                && !string.IsNullOrWhiteSpace(e.State.Argument))
            {
                string path = e.State.Argument!;
                _decks.TryGetValue(e.Slot, out DeckSessionState? existing);
                bool samePath = existing is not null
                    && string.Equals(existing.TrackPath, path, StringComparison.OrdinalIgnoreCase);

                // Preserve a previously-analyzed anchor when a no-analysis re-load (BPM = 0) of the SAME
                // track arrives — otherwise a second startup loader wipes the saved BPM/first-beat to 0.
                double bpm = e.State.Value;
                double firstBeat = 0;
                double downbeat = 0;
                if (samePath)
                {
                    if (bpm == 0)
                        bpm = existing!.Bpm;
                    firstBeat = existing!.FirstBeatSeconds;
                    downbeat = existing!.DownbeatSeconds;
                }

                _decks[e.Slot] = new DeckSessionState(e.Slot, path, bpm, firstBeat, downbeat);
                QueueSaveLocked();
            }
            else if (e.Kind == PerformanceActionKind.DeckSetFirstBeat
                     && _decks.TryGetValue(e.Slot, out DeckSessionState? deck))
            {
                // A first-beat reset to 0 from a no-analysis re-load must not erase a saved anchor; only
                // overwrite when the incoming anchor is non-zero or none was saved yet.
                if (e.State.Value != 0 || deck.FirstBeatSeconds == 0)
                {
                    _decks[e.Slot] = deck with { FirstBeatSeconds = e.State.Value };
                    QueueSaveLocked();
                }
            }
            else if (e.Kind == PerformanceActionKind.DeckSetDownbeat
                     && _decks.TryGetValue(e.Slot, out DeckSessionState? downbeatDeck))
            {
                // Persist a manually-set downbeat ("one") so it survives a restart. As with the first-beat
                // anchor, a reset to 0 must not erase a saved one — only overwrite on a non-zero edit (or the
                // first time). The auto-resolved downbeat is NOT persisted (it re-derives from the catalog).
                if (e.State.Value != 0 && e.State.Value != downbeatDeck.DownbeatSeconds)
                {
                    _decks[e.Slot] = downbeatDeck with { DownbeatSeconds = e.State.Value };
                    QueueSaveLocked();
                }
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
            task => _logger.LogWarning(
                task.Exception?.GetBaseException(), "Deck session could not be saved."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _retryTimer?.Dispose();
        _dispatcher.FeedbackChanged -= OnFeedbackChanged;
        try
        {
            _pendingSave.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deck session could not finish saving during shutdown.");
        }
    }
}
