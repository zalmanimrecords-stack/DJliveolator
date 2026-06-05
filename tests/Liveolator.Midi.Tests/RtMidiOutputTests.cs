using Liveolator.Core.Mapping;
using Liveolator.Midi;
using Liveolator.Midi.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Midi.Tests;

/// <summary>
/// The output adapter turns Core feedback MidiMessages into RtMidi note/CC sends and forwards raw
/// SysEx, opening the device lazily and never letting a send failure escape (doc 06).
/// </summary>
public sealed class RtMidiOutputTests
{
    private static RtMidiOutput Output(FakeOutputDevice device)
        => new(device, NullLogger.Instance);

    [Fact]
    public void Send_NoteOn_maps_to_device_note_on_and_opens_lazily()
    {
        var device = new FakeOutputDevice("Push");
        var output = Output(device);

        output.Send(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 21));

        Assert.True(device.IsOpen); // opened on first send
        Assert.Equal(1, device.NoteOnCount);
        Assert.Equal(21, device.LastNoteOn!.Value.Velocity);
        Assert.Equal(36, (int)device.LastNoteOn!.Value.Key);
    }

    [Fact]
    public void Send_ControlChange_maps_to_device_control_change()
    {
        var device = new FakeOutputDevice("Push");
        Output(device).Send(new MidiMessage(MidiMessageType.ControlChange, 1, 20, 127));

        Assert.Equal(1, device.ControlChangeCount);
        Assert.Equal(20, device.LastControlChange!.Value.Control);
        Assert.Equal(127, device.LastControlChange!.Value.Value);
    }

    [Fact]
    public void Send_PitchBend_is_ignored_as_unsupported_feedback()
    {
        var device = new FakeOutputDevice("Push");
        Output(device).Send(new MidiMessage(MidiMessageType.PitchBend, 0, 0, 64));

        Assert.Equal(0, device.NoteOnCount);
        Assert.Equal(0, device.ControlChangeCount);
    }

    [Fact]
    public void SendSysEx_forwards_payload_to_the_device()
    {
        var device = new FakeOutputDevice("Push");
        // A full framed SysEx as a caller would supply it (Push LCD/mode bytes, doc 06).
        var framed = new byte[] { 0xF0, 0x47, 0x7F, 0xF7 };
        // RtMidi.Core's SysExMessage stores the payload WITHOUT the F0/F7 framing (it re-adds them
        // on the wire), so the device records the inner bytes — what matters is they pass through.
        var inner = new byte[] { 0x47, 0x7F };

        Output(device).SendSysEx(framed);

        Assert.Equal(inner, device.LastSysEx);
    }

    [Fact]
    public void Send_does_not_throw_when_device_open_fails()
    {
        var device = new FakeOutputDevice("Push") { OpenResult = false };
        var output = Output(device);

        var ex = Record.Exception(() => output.Send(new MidiMessage(MidiMessageType.NoteOn, 0, 1, 1)));

        Assert.Null(ex);
        Assert.Equal(0, device.NoteOnCount); // never sent because the port could not open
    }

    [Fact]
    public void Send_after_dispose_is_a_noop()
    {
        var device = new FakeOutputDevice("Push");
        var output = Output(device);
        output.Dispose();

        output.Send(new MidiMessage(MidiMessageType.NoteOn, 0, 1, 1));

        Assert.True(device.Disposed);
        Assert.Equal(0, device.NoteOnCount);
    }
}
