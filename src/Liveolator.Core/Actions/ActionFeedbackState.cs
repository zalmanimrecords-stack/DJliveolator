namespace Liveolator.Core.Actions;

/// <summary>
/// The current state of an action, reported back so controllers can light pads/LEDs (doc 06)
/// and the UI can reflect armed/active/value without polling (doc 12).
/// </summary>
/// <param name="IsActive">Toggle is on, or the action is armed/engaged.</param>
/// <param name="IsAvailable">The action can be triggered right now.</param>
/// <param name="Value">Current value for knob-/fader-backed actions, in 0..1.</param>
/// <param name="Argument">Optional free-form payload mirroring <see cref="PerformanceAction.Argument"/>,
/// for state that is not a bool/number — e.g. the loaded track path a deck reports after a load. Null
/// when unused.</param>
public sealed record ActionFeedbackState(bool IsActive, bool IsAvailable, double Value, string? Argument = null)
{
    /// <summary>
    /// True when automation (e.g. an AUTO crossfade) moved <see cref="Value"/> away from where a bound physical
    /// control sits, so the controller mapper holds that control until it reaches the value instead of letting
    /// the first touch jump it back. Non-positional so every existing construction stays valid.
    /// </summary>
    public bool RequiresPickup { get; init; }

    /// <summary>The state for an action that has no owning handler or cannot currently run.</summary>
    public static ActionFeedbackState Unavailable { get; } = new(IsActive: false, IsAvailable: false, Value: 0);
}
