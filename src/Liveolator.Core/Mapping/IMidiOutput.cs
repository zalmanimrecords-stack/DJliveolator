namespace Liveolator.Core.Mapping;

/// <summary>
/// One open MIDI output device, used to drive controller LEDs/displays from action feedback
/// (doc 05). SysEx is separate because Push mode/LCD control needs it (doc 06). Output is a
/// distinct concern from input, behind its own seam.
/// </summary>
public interface IMidiOutput : IDisposable
{
    /// <summary>The device this output is bound to.</summary>
    string DeviceName { get; }

    /// <summary>Sends a single channel-voice message (note/CC) — e.g. to set a pad LED.</summary>
    void Send(MidiMessage message);

    /// <summary>Sends a raw SysEx payload — e.g. Push mode switch or LCD text.</summary>
    void SendSysEx(ReadOnlyMemory<byte> data);
}
