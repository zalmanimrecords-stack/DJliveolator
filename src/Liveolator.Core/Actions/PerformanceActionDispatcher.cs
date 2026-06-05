using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Actions;

/// <summary>
/// Default dispatcher: builds a kind→handler map from the registered handlers at construction
/// (rejecting any kind claimed by two handlers, so a misconfiguration fails fast) and routes
/// each action there. There is deliberately no per-kind switch — routing is data, owned by the
/// handlers (doc 04, global standards #2/#3). Handler failures are logged with the action in
/// context and swallowed so one bad action never tears down the input pipeline (#16, #26);
/// unknown kinds are logged as a warning rather than thrown.
/// </summary>
public sealed class PerformanceActionDispatcher : IPerformanceActionDispatcher, IDisposable
{
    private readonly IReadOnlyDictionary<PerformanceActionKind, IPerformanceActionHandler> _routes;
    private readonly IReadOnlyList<IPerformanceActionHandler> _handlers;
    private readonly IActionFeedbackSynchronizer _synchronizer;
    private readonly ILogger<PerformanceActionDispatcher> _logger;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<ActionFeedbackChanged>? FeedbackChanged;

    /// <param name="handlers">The concern handlers; every kind must be owned by exactly one.</param>
    /// <param name="logger">Sink for handler failures and unhandled-kind warnings.</param>
    /// <param name="synchronizer">Marshals feedback notifications to the subscriber thread;
    /// defaults to inline execution when null.</param>
    /// <exception cref="ArgumentException">Two handlers claim the same kind.</exception>
    public PerformanceActionDispatcher(
        IEnumerable<IPerformanceActionHandler> handlers,
        ILogger<PerformanceActionDispatcher> logger,
        IActionFeedbackSynchronizer? synchronizer = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizer = synchronizer ?? InlineActionFeedbackSynchronizer.Instance;
        _handlers = handlers.ToList();

        var routes = new Dictionary<PerformanceActionKind, IPerformanceActionHandler>();
        foreach (IPerformanceActionHandler handler in _handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            foreach (PerformanceActionKind kind in handler.HandledKinds)
            {
                if (routes.ContainsKey(kind))
                    throw new ArgumentException(
                        $"Action kind '{kind}' is claimed by more than one handler.", nameof(handlers));
                routes[kind] = handler;
            }

            handler.FeedbackChanged += OnHandlerFeedbackChanged;
        }

        _routes = routes;
    }

    /// <inheritdoc />
    public void Dispatch(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_routes.TryGetValue(action.Kind, out IPerformanceActionHandler? handler))
        {
            _logger.LogWarning("No handler registered for action kind {Kind}; ignoring.", action.Kind);
            return;
        }

        try
        {
            handler.Handle(action);
        }
        catch (Exception ex)
        {
            // Surface, never silently drop: log with the action in context and keep the
            // dispatcher alive for the next action.
            _logger.LogError(ex, "Handler failed applying action {Kind} (slot {Slot}, mode {Mode}).",
                action.Kind, action.Slot, action.InputMode);
        }
    }

    /// <inheritdoc />
    public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
    {
        if (!_routes.TryGetValue(kind, out IPerformanceActionHandler? handler))
            return ActionFeedbackState.Unavailable;

        try
        {
            return handler.GetFeedback(kind, slot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler failed reporting feedback for {Kind} (slot {Slot}).", kind, slot);
            return ActionFeedbackState.Unavailable;
        }
    }

    private void OnHandlerFeedbackChanged(object? sender, ActionFeedbackChanged e)
        => _synchronizer.Post(() => FeedbackChanged?.Invoke(this, e));

    /// <summary>Unsubscribes from handler feedback so the dispatcher can be replaced cleanly.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (IPerformanceActionHandler handler in _handlers)
            handler.FeedbackChanged -= OnHandlerFeedbackChanged;

        _disposed = true;
    }
}
