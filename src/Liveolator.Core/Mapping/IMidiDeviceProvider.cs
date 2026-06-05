namespace Liveolator.Core.Mapping;

/// <summary>
/// Enumerates the MIDI devices currently attached, for the Mappings UI to list and the engine to
/// auto-select a profile by name (doc 05/12). Backed by the MIDI library in a binding project.
/// </summary>
public interface IMidiDeviceProvider
{
    /// <summary>Names of available input devices.</summary>
    IReadOnlyList<string> GetInputDeviceNames();

    /// <summary>Names of available output (feedback-capable) devices.</summary>
    IReadOnlyList<string> GetOutputDeviceNames();
}
