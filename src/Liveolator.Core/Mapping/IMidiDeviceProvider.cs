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
    /// Opens the first input device whose name matches <paramref name="deviceName"/> (matching is the
    /// implementation's affair — the binding matches case-insensitively by substring), or null when
    /// none match. The returned input is not yet open — the caller calls <see cref="IMidiInput.Open"/>.
    /// Promoted onto the seam so the runtime routing pipeline (<c>MidiControlSession</c>) composes
    /// against the abstraction and unit-tests with a fake.
    /// </summary>
    IMidiInput? OpenInput(string deviceName);

    /// <summary>
    /// Opens the first output (feedback/LED) device matching <paramref name="deviceName"/>, or null
    /// when none match. Feedback is optional — control still works without an output (doc 06).
    /// </summary>
    IMidiOutput? OpenOutput(string deviceName);
}
