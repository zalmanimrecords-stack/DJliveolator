using RtMidi.Core.Devices;
using RtMidi.Core.Messages;

namespace Liveolator.Midi.Tests.Fakes;

/// <summary>
/// Test double for RtMidi.Core's <see cref="IMidiOutputDevice"/>. Records the note/CC/SysEx sends
/// the feedback path uses so <see cref="RtMidiOutput"/> is verified with no native library present.
/// </summary>
internal sealed class FakeOutputDevice : IMidiOutputDevice
{
    public FakeOutputDevice(string name) => Name = name;

    public string Name { get; }
    public bool IsOpen { get; private set; }
    public bool OpenResult { get; set; } = true;
    public bool SendResult { get; set; } = true;
    public bool Disposed { get; private set; }

    public int NoteOnCount { get; private set; }
    public int NoteOffCount { get; private set; }
    public int ControlChangeCount { get; private set; }
    public NoteOnMessage? LastNoteOn { get; private set; }
    public ControlChangeMessage? LastControlChange { get; private set; }
    public byte[]? LastSysEx { get; private set; }

    public bool Open()
    {
        IsOpen = OpenResult;
        return OpenResult;
    }

    public void Close() => IsOpen = false;
    public void Dispose() => Disposed = true;

    public bool Send(in NoteOffMessage m) { NoteOffCount++; return SendResult; }

    public bool Send(in NoteOnMessage m)
    {
        NoteOnCount++;
        LastNoteOn = m;
        return SendResult;
    }

    public bool Send(in PolyphonicKeyPressureMessage m) => SendResult;

    public bool Send(in ControlChangeMessage m)
    {
        ControlChangeCount++;
        LastControlChange = m;
        return SendResult;
    }

    public bool Send(in ProgramChangeMessage m) => SendResult;
    public bool Send(in ChannelPressureMessage m) => SendResult;
    public bool Send(in PitchBendMessage m) => SendResult;
    public bool Send(in NrpnMessage m) => SendResult;

    public bool Send(in SysExMessage m)
    {
        LastSysEx = m.Data;
        return SendResult;
    }

    public bool Send(in MidiTimeCodeQuarterFrameMessage m) => SendResult;
    public bool Send(in SongPositionPointerMessage m) => SendResult;
    public bool Send(in SongSelectMessage m) => SendResult;
    public bool Send(in TuneRequestMessage m) => SendResult;
}
