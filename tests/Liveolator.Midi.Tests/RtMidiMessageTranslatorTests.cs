using Liveolator.Core.Mapping;
using Liveolator.Midi;
using RtMidi.Core.Enums;
using RtMidi.Core.Messages;

namespace Liveolator.Midi.Tests;

/// <summary>
/// The load-bearing translation: RtMidi.Core message structs -> Core-neutral MidiMessage. These run
/// with no MIDI device and no native rtmidi library present — the structs are plain managed values.
/// </summary>
public sealed class RtMidiMessageTranslatorTests
{
    [Fact]
    public void NoteOn_maps_channel_key_and_velocity()
    {
        var src = new NoteOnMessage(Channel.Channel1, (Key)60, 100);

        MidiMessage result = RtMidiMessageTranslator.FromNoteOn(src);

        Assert.Equal(MidiMessageType.NoteOn, result.Type);
        Assert.Equal(0, result.Channel); // RtMidi Channel1 == 0, matching Core's 0-based channel
        Assert.Equal(60, result.Data1);
        Assert.Equal(100, result.Data2);
    }

    [Fact]
    public void NoteOn_velocity_zero_is_preserved_as_NoteOn_for_Core_to_interpret()
    {
        // Core's BindingMatcher treats NoteOn vel0 as NoteOff; the translator must not pre-collapse.
        var src = new NoteOnMessage(Channel.Channel10, (Key)36, 0);

        MidiMessage result = RtMidiMessageTranslator.FromNoteOn(src);

        Assert.Equal(MidiMessageType.NoteOn, result.Type);
        Assert.Equal(9, result.Channel);
        Assert.Equal(0, result.Data2);
    }

    [Fact]
    public void NoteOff_maps_channel_key_and_velocity()
    {
        var src = new NoteOffMessage(Channel.Channel16, (Key)99, 64);

        MidiMessage result = RtMidiMessageTranslator.FromNoteOff(src);

        Assert.Equal(MidiMessageType.NoteOff, result.Type);
        Assert.Equal(15, result.Channel);
        Assert.Equal(99, result.Data1);
        Assert.Equal(64, result.Data2);
    }

    [Fact]
    public void ControlChange_maps_channel_control_and_value()
    {
        var src = new ControlChangeMessage(Channel.Channel2, control: 21, value: 127);

        MidiMessage result = RtMidiMessageTranslator.FromControlChange(src);

        Assert.Equal(MidiMessageType.ControlChange, result.Type);
        Assert.Equal(1, result.Channel);
        Assert.Equal(21, result.Data1);
        Assert.Equal(127, result.Data2);
    }

    [Theory]
    [InlineData(0, 0, 0)]        // minimum
    [InlineData(8192, 0, 64)]    // center: LSB 0, MSB 64
    [InlineData(16383, 127, 127)] // maximum
    public void PitchBend_splits_14bit_value_into_7bit_lsb_and_msb(int value14, int expectedLsb, int expectedMsb)
    {
        var src = new PitchBendMessage(Channel.Channel1, value14);

        MidiMessage result = RtMidiMessageTranslator.FromPitchBend(src);

        Assert.Equal(MidiMessageType.PitchBend, result.Type);
        Assert.Equal(0, result.Channel);
        Assert.Equal(expectedLsb, result.Data1);
        Assert.Equal(expectedMsb, result.Data2);
    }

    [Fact]
    public void PitchBend_lsb_msb_recombine_to_original_14bit_value()
    {
        const int value14 = 12345;
        var src = new PitchBendMessage(Channel.Channel5, value14);

        MidiMessage result = RtMidiMessageTranslator.FromPitchBend(src);

        int recombined = result.Data1 | (result.Data2 << 7);
        Assert.Equal(value14, recombined);
    }
}
