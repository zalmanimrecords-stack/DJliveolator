using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Liveolator.Core.Visuals;

namespace Liveolator.App.Composition;

/// <summary>
/// Autosaves the live visual layer arrangement so the Live tab opens with the same layer sources it
/// was last left with, instead of resetting to the shipped starter scene on every launch.
///
/// Layer-source swaps mutate only the engine's in-memory active scene (<c>SetLayerSource</c>); nothing
/// is written to disk. This listener mirrors <see cref="DeckSessionPersistence"/>: it watches the one
/// dispatcher for <see cref="PerformanceActionKind.VisualSetLayerSource"/> feedback and persists the
/// current active scene under the well-known startup bank name (<c>"Live"</c>). Restore needs no code
/// here — <c>ServiceConfig.LoadBanksOrStarter</c> already loads the <c>"Live"</c> bank first at startup,
/// so the engine is constructed from the saved arrangement and the UI seeds its dropdowns from it.
///
/// Saves are queued (never blocking the dispatch thread) and faults are logged, never thrown — a write
/// failure loses at most the last edit (global standards #16/#26).
/// </summary>
internal sealed class VisualSessionPersistence : IDisposable
{
    /// <summary>The well-known startup bank name the composition root loads first (doc 13/22 C3).</summary>
    public const string LiveBankName = "Live";

    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly Func<VisualScene?> _currentScene;
    private readonly ILiveProfileStore _store;
    private readonly object _gate = new();
    private Task _pendingSave = Task.CompletedTask;
    private bool _disposed;

    /// <param name="dispatcher">The single dispatcher whose feedback echoes engine state changes.</param>
    /// <param name="currentScene">Snapshot of the engine's active scene to persist; null until a scene is active.</param>
    /// <param name="store">The Live-Mode profile store that owns the on-disk scene banks.</param>
    public VisualSessionPersistence(
        IPerformanceActionDispatcher dispatcher,
        Func<VisualScene?> currentScene,
        ILiveProfileStore store)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _currentScene = currentScene ?? throw new ArgumentNullException(nameof(currentScene));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        _dispatcher.FeedbackChanged += OnFeedbackChanged;
    }

    private void OnFeedbackChanged(object? sender, ActionFeedbackChanged e)
    {
        if (e.Kind != PerformanceActionKind.VisualSetLayerSource)
            return;

        VisualScene? scene = _currentScene();
        if (scene is null)
            return;

        // Persist under the well-known startup name so LoadBanksOrStarter restores it as the active bank.
        var bank = new VisualBank(LiveBankName, new[] { scene });
        lock (_gate)
            QueueSaveLocked(bank);
    }

    private void QueueSaveLocked(VisualBank bank)
    {
        _pendingSave = _pendingSave.ContinueWith(
            _ => _store.SaveVisualBankAsync(bank),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
        _ = _pendingSave.ContinueWith(
            task => System.Diagnostics.Trace.TraceWarning(
                $"Visual layer arrangement could not be saved: {task.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>Awaits the most recently queued save. Used by tests and by shutdown flushing.</summary>
    public Task WaitForPendingSaveAsync(TimeSpan timeout)
    {
        Task pending;
        lock (_gate)
            pending = _pendingSave;
        return pending.WaitAsync(timeout);
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
                $"Visual layer arrangement could not finish saving during shutdown: {ex.Message}.");
        }
        _disposed = true;
    }
}
