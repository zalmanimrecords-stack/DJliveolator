namespace Liveolator.Core.Actions;

/// <summary>
/// The current state of an action, reported back so controllers can light pads/LEDs (doc 06)
/// and the UI can reflect armed/active/value without polling (doc 12).
/// </summary>
/// <param name="IsActive">Toggle is on, or the action is armed/engaged.</param>
/// <param name="IsAvailable">The action can be triggered right now.</param>
/// <param name="Value">Current value for knob-/fader-backed actions, in 0..1.</param>
public sealed record ActionFeedbackState(bool IsActive, bool IsAvailable, double Value)
{
    /// <summary>The state for an action that has no owning handler or cannot currently run.</summary>
    public static ActionFeedbackState Unavailable { get; } = new(IsActive: false, IsAvailable: false, Value: 0);
}
