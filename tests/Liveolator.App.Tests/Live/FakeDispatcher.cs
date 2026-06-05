using System;
using System.Collections.Generic;
using Liveolator.Core.Actions;

namespace Liveolator.App.Tests.Live;

/// <summary>
/// A test double for <see cref="IPerformanceActionDispatcher"/> used by the Live module view-model
/// tests: it records every dispatched action, returns seeded feedback for <c>GetFeedback</c>, and can
/// raise <c>FeedbackChanged</c> to simulate a controller move or an engine echo.
/// </summary>
public sealed class FakeDispatcher : IPerformanceActionDispatcher
{
    private readonly Dictionary<(PerformanceActionKind, int), ActionFeedbackState> _feedback = new();

    public List<PerformanceAction> Dispatched { get; } = new();

    public void Dispatch(PerformanceAction action) => Dispatched.Add(action);

    public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
        => _feedback.TryGetValue((kind, slot), out ActionFeedbackState? state) ? state : ActionFeedbackState.Unavailable;

    public void SeedFeedback(PerformanceActionKind kind, int slot, ActionFeedbackState state)
        => _feedback[(kind, slot)] = state;

    public event EventHandler<ActionFeedbackChanged>? FeedbackChanged;

    public void RaiseFeedback(PerformanceActionKind kind, int slot, ActionFeedbackState state)
        => FeedbackChanged?.Invoke(this, new ActionFeedbackChanged(kind, slot, state));
}
