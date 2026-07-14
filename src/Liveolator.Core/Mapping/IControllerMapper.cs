namespace Liveolator.Core.Mapping;

/// <summary>
/// Translates inbound MIDI into performance actions using the active profile, then hands them to
/// the dispatcher. No controller code ever touches an engine directly (doc 05).
/// </summary>
public interface IControllerMapper
{
    /// <summary>The profile currently in effect.</summary>
    ControllerMappingProfile ActiveProfile { get; }

    /// <summary>Swaps the active profile (e.g. on device reconnect or user selection).</summary>
    void SetProfile(ControllerMappingProfile profile);

    /// <summary>Matches <paramref name="message"/> to a binding and dispatches the resulting action.</summary>
    void Apply(MidiMessage message);
}
