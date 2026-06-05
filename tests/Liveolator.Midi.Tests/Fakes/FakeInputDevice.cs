using RtMidi.Core.Devices;
using RtMidi.Core.Devices.Nrpn;
using RtMidi.Core.Messages;

namespace Liveolator.Midi.Tests.Fakes;

/// <summary>
/// Test double for RtMidi.Core's <see cref="IMidiInputDevice"/>. Implements the full interface but
/// only the message events the adapter uses (NoteOn/NoteOff/ControlChange/PitchBend) are exercised;
/// tests raise them via the <c>Emit*</c> helpers to drive <see cref="RtMidiInput"/> with no native
/// library present.
/// </summary>
internal sealed class FakeInputDevice : IMidiInputDevice
{
    public FakeInputDevice(string name) => Name = name;

    public string Name { get; }
    public bool IsOpen { get; private set; }
    public bool OpenResult { get; set; } = true;
    public bool Disposed { get; private set; }

    public bool Open()
    {
        IsOpen = OpenResult;
        return OpenResult;
    }

    public void Close() => IsOpen = false;
    public void Dispose() => Disposed = true;

    public void SetNrpnMode(NrpnMode mode) { }

    public event NoteOffMessageHandler? NoteOff;
    public event NoteOnMessageHandler? NoteOn;
    public event PolyphonicKeyPressureMessageHandler? PolyphonicKeyPressure;
    public event ControlChangeMessageHandler? ControlChange;
    public event ProgramChangeMessageHandler? ProgramChange;
    public event ChannelPressureMessageHandler? ChannelPressure;
    public event PitchBendMessageHandler? PitchBend;
    public event NrpnMessageHandler? Nrpn;
    public event SysExMessageHandler? SysEx;
    public event MidiTimeCodeQuarterFrameHandler? MidiTimeCodeQuarterFrame;
    public event SongPositionPointerHandler? SongPositionPointer;
    public event SongSelectHandler? SongSelect;
    public event TuneRequestHandler? TuneRequest;

    public void EmitNoteOn(in NoteOnMessage msg) => NoteOn?.Invoke(this, msg);
    public void EmitNoteOff(in NoteOffMessage msg) => NoteOff?.Invoke(this, msg);
    public void EmitControlChange(in ControlChangeMessage msg) => ControlChange?.Invoke(this, msg);
    public void EmitPitchBend(in PitchBendMessage msg) => PitchBend?.Invoke(this, msg);

    // Referenced to suppress unused-event warnings for the shapes the adapter ignores.
    public bool HasUnusedSubscribers =>
        PolyphonicKeyPressure is not null || ProgramChange is not null || ChannelPressure is not null
        || Nrpn is not null || SysEx is not null || MidiTimeCodeQuarterFrame is not null
        || SongPositionPointer is not null || SongSelect is not null || TuneRequest is not null;
}
