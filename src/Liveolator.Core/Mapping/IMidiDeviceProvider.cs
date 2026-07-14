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

    /// <summary>
    /// Opens the first input device whose name contains <paramref name="deviceName"/>
    /// (case-insensitive), as a Core <see cref="IMidiInput"/> the host routes into the dispatcher.
    /// Returns null when no device matches, so the host can degrade to running without a controller
    /// (doc 05/12). The returned input is not yet started — the caller invokes <see cref="IMidiInput.Open"/>.
    /// </summary>
    IMidiInput? OpenInput(string deviceName);

    /// <summary>
    /// Opens the first output (feedback) device matching <paramref name="deviceName"/>, or null when
    /// none match. Output is optional — control still works without LED feedback (doc 06).
    /// </summary>
    IMidiOutput? OpenOutput(string deviceName);
}
