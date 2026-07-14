namespace Liveolator.Core.Actions;

/// <summary>
/// The single entry point through which every input source drives the engines. Routes each
/// action to its owning handler, exposes current feedback for LEDs/UI, and raises
/// <see cref="FeedbackChanged"/> as state evolves. Decoupling input from engines this way keeps
/// adding controllers and autopilot free of engine changes, and makes each action testable in
/// isolation (doc 04).
/// </summary>
public interface IPerformanceActionDispatcher
{
    /// <summary>Routes <paramref name="action"/> to its owning handler.</summary>
    void Dispatch(PerformanceAction action);

    /// <summary>Returns the current feedback state for a kind/slot, for LEDs and UI indicators.</summary>
    ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0);

    /// <summary>Raised when any handled action changes feedback state.</summary>
    event EventHandler<ActionFeedbackChanged>? FeedbackChanged;

    /// <summary>
    /// Raised for every dispatched action (on the dispatching thread, before routing). Lets
    /// automation observe live input and yield to a human gesture on a parameter it is driving
    /// (filtered via <see cref="PerformanceAction.Origin"/>) — never to re-route or veto actions.
    /// </summary>
    event EventHandler<PerformanceAction>? ActionDispatched;
}
