namespace Liveolator.Core.Mapping;

/// <summary>
/// Read-only view of the live MIDI controller connection for status UIs (the shell's top-bar
/// indicators): which device is connected and a pulse each time a message arrives. Implemented by
/// <see cref="MidiControlSession"/>; the seam lets the App's status view-model bind without depending
/// on the orchestration internals and unit-test with a trivial fake.
/// </summary>
public interface IMidiControlStatus
{
    /// <summary>True while the controller input is open and routing.</summary>
    bool IsInputConnected { get; }

    /// <summary>The opened controller's device name, or null when idle.</summary>
    string? InputDeviceName { get; }

    /// <summary>True while a feedback (LED) output is open.</summary>
    bool IsOutputConnected { get; }

    /// <summary>The opened feedback device's name, or null when none.</summary>
    string? OutputDeviceName { get; }

    /// <summary>
    /// Raised on each inbound MIDI message. Fires on the MIDI callback thread — subscribers must
    /// marshal to their own thread.
    /// </summary>
    event EventHandler? ActivityDetected;
}
