using Liveolator.Core.Mapping;
using Liveolator.Midi;
using Liveolator.Midi.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using RtMidi.Core.Enums;
using RtMidi.Core.Messages;

namespace Liveolator.Midi.Tests;

/// <summary>
/// The input adapter translates RtMidi device events to Core MidiMessages and re-raises them, with
/// no native library — the fake device stands in for rtmidi.
/// </summary>
public sealed class RtMidiInputTests
{
    private static RtMidiInput Open(FakeInputDevice device)
    {
        var input = new RtMidiInput(device, NullLogger.Instance);
        input.Open();
        return input;
    }

    [Fact]
    public void Forwards_translated_messages_while_open()
    {
        var device = new FakeInputDevice("Push");
        using var input = Open(device);
        var received = new List<MidiMessage>();
        input.MessageReceived += (_, m) => received.Add(m);

        device.EmitNoteOn(new NoteOnMessage(Channel.Channel1, (Key)40, 120));
        device.EmitControlChange(new ControlChangeMessage(Channel.Channel2, 7, 64));
        device.EmitPitchBend(new PitchBendMessage(Channel.Channel1, 8192));

        Assert.Collection(received,
            m => Assert.Equal(new MidiMessage(MidiMessageType.NoteOn, 0, 40, 120), m),
            m => Assert.Equal(new MidiMessage(MidiMessageType.ControlChange, 1, 7, 64), m),
            m => Assert.Equal(new MidiMessage(MidiMessageType.PitchBend, 0, 0, 64), m));
    }

    [Fact]
    public void Open_sets_IsOpen()
    {
        var device = new FakeInputDevice("Push");
        using var input = Open(device);
        Assert.True(input.IsOpen);
    }

    [Fact]
    public void Open_throws_when_device_reports_failure()
    {
        var device = new FakeInputDevice("Push") { OpenResult = false };
        var input = new RtMidiInput(device, NullLogger.Instance);

        Assert.Throws<InvalidOperationException>(() => input.Open());
    }

    [Fact]
    public void A_throwing_handler_does_not_propagate_into_the_callback()
    {
        var device = new FakeInputDevice("Push");
        using var input = Open(device);
        input.MessageReceived += (_, _) => throw new InvalidOperationException("handler bug");

        // Must not throw back into the native callback (would crash rtmidi).
        var ex = Record.Exception(() => device.EmitNoteOn(new NoteOnMessage(Channel.Channel1, (Key)1, 1)));
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_unsubscribes_and_disposes_device()
    {
        var device = new FakeInputDevice("Push");
        var input = Open(device);
        var received = new List<MidiMessage>();
        input.MessageReceived += (_, m) => received.Add(m);

        input.Dispose();
        device.EmitNoteOn(new NoteOnMessage(Channel.Channel1, (Key)1, 1));

        Assert.True(device.Disposed);
        Assert.Empty(received); // no longer subscribed to the device
    }
}
