namespace Liveolator.Core.Mapping;

/// <summary>
/// One open MIDI input device, surfaced as a stream of normalized <see cref="MidiMessage"/>s. The
/// concrete library-backed implementation lives in a binding project; Core depends only on this
/// seam so routing logic unit-tests without any device (doc 05, same pattern as IFileEnumerator).
/// </summary>
public interface IMidiInput : IDisposable
{
    /// <summary>The device this input is bound to (used for profile auto-selection).</summary>
    string DeviceName { get; }

    /// <summary>True between a successful <see cref="Open"/> and <see cref="Close"/>.</summary>
    bool IsOpen { get; }

    /// <summary>Begins delivering messages. Implementations log and surface device errors.</summary>
    void Open();

    /// <summary>Stops delivering messages; safe to call when already closed.</summary>
    void Close();

    /// <summary>Raised for each inbound message while open.</summary>
    event EventHandler<MidiMessage>? MessageReceived;
}
