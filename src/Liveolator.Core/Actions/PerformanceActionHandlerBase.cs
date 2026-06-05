namespace Liveolator.Core.Actions;

/// <summary>
/// Convenience base for handlers: implements the <see cref="FeedbackChanged"/> event and a
/// <see cref="RaiseFeedback"/> helper so concrete handlers only write their routing and engine
/// logic. Feedback defaults to <see cref="ActionFeedbackState.Unavailable"/> until a handler
/// overrides <see cref="GetFeedback"/>.
/// </summary>
public abstract class PerformanceActionHandlerBase : IPerformanceActionHandler
{
    /// <inheritdoc />
    public abstract IReadOnlySet<PerformanceActionKind> HandledKinds { get; }

    /// <inheritdoc />
    public abstract void Handle(PerformanceAction action);

    /// <inheritdoc />
    public virtual ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
        => ActionFeedbackState.Unavailable;

    /// <inheritdoc />
    public event EventHandler<ActionFeedbackChanged>? FeedbackChanged;

    /// <summary>Notifies subscribers that one of this handler's actions changed state.</summary>
    protected void RaiseFeedback(PerformanceActionKind kind, int slot, ActionFeedbackState state)
        => FeedbackChanged?.Invoke(this, new ActionFeedbackChanged(kind, slot, state));
}
