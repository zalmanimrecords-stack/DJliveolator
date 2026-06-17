using System;
using Liveolator.Midi;

namespace Liveolator.Midi.Tests;

/// <summary>
/// Pure byte-formatting tests for the Ableton Push 1 feedback bytes (doc 06). Push 1 LED control is
/// mostly plain MIDI: pad LEDs are NoteOn (velocity = color palette index, channel = animation), and
/// button LEDs are CC. The LCD strip and the User-mode switch are the only SysEx parts. These helpers
/// only format byte arrays  -  the actual device send is the existing IMidiOutput path (manual-verify).
/// </summary>
public sealed class Push1SysexTests
{
    [Fact]
    public void PadLed_solid_color_is_a_noteon_on_the_solid_channel()
    {
        // Pad index 0 -> note 36; solid animation uses channel 0; velocity = color index.
        Liveolator.Core.Mapping.MidiMessage msg = Push1Sysex.PadLed(pad: 0, color: 21, blink: Push1PadAnimation.Solid);

        Assert.Equal(Liveolator.Core.Mapping.MidiMessageType.NoteOn, msg.Type);
        Assert.Equal(0, msg.Channel);
        Assert.Equal(36, msg.Data1);
        Assert.Equal(21, msg.Data2);
    }

    [Fact]
    public void PadLed_blink_uses_a_nonzero_animation_channel()
    {
        // Blink animations live on higher channels (doc 06: channel selects the animation rate).
        Liveolator.Core.Mapping.MidiMessage msg = Push1Sysex.PadLed(pad: 63, color: 5, blink: Push1PadAnimation.BlinkFast);

        Assert.Equal(Liveolator.Core.Mapping.MidiMessageType.NoteOn, msg.Type);
        Assert.NotEqual(0, msg.Channel);
        Assert.Equal(99, msg.Data1); // pad 63 -> note 36 + 63
        Assert.Equal(5, msg.Data2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(64)]
    public void PadLed_rejects_pad_index_outside_the_grid(int pad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Push1Sysex.PadLed(pad, color: 1, blink: Push1PadAnimation.Solid));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void PadLed_rejects_color_outside_the_palette(int color)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Push1Sysex.PadLed(pad: 0, color, blink: Push1PadAnimation.Solid));
    }

    [Fact]
    public void ButtonLed_is_a_control_change_on_the_push_channel()
    {
        Liveolator.Core.Mapping.MidiMessage msg = Push1Sysex.ButtonLed(cc: 60, value: 127);

        Assert.Equal(Liveolator.Core.Mapping.MidiMessageType.ControlChange, msg.Type);
        Assert.Equal(0, msg.Channel);
        Assert.Equal(60, msg.Data1);
        Assert.Equal(127, msg.Data2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void ButtonLed_rejects_value_outside_7bit_range(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Push1Sysex.ButtonLed(cc: 60, value));
    }

    [Fact]
    public void SetUserMode_is_the_exact_documented_sysex_frame()
    {
        // Push 1 set-mode SysEx: F0 47 7F 15 62 00 01 <mode> F7, mode 01 = User, 00 = Live.
        byte[] expected = { 0xF0, 0x47, 0x7F, 0x15, 0x62, 0x00, 0x01, 0x01, 0xF7 };

        byte[] actual = Push1Sysex.SetUserMode(userMode: true);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SetUserMode_live_flips_only_the_mode_byte()
    {
        byte[] expected = { 0xF0, 0x47, 0x7F, 0x15, 0x62, 0x00, 0x01, 0x00, 0xF7 };

        Assert.Equal(expected, Push1Sysex.SetUserMode(userMode: false));
    }

    [Fact]
    public void LcdText_frames_the_header_line_offset_ascii_and_terminator()
    {
        // Write "OK" to display line 0 at column 0. Push 1 LCD write:
        // F0 47 7F 15 <18+line> 00 <len> <offset> <ascii...> F7
        byte[] actual = Push1Sysex.LcdText(line: 0, offset: 0, text: "OK");

        byte[] expected =
        {
            0xF0, 0x47, 0x7F, 0x15,
            0x18,       // 0x18 + line(0) = display-line write command
            0x00,       // reserved
            0x03,       // length = offset-byte + 2 chars
            0x00,       // column offset
            (byte)'O', (byte)'K',
            0xF7,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LcdText_command_byte_tracks_the_line_index()
    {
        byte[] line3 = Push1Sysex.LcdText(line: 3, offset: 0, text: "X");

        Assert.Equal(0x18 + 3, line3[4]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void LcdText_rejects_line_outside_the_four_row_display(int line)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Push1Sysex.LcdText(line, offset: 0, text: "x"));
    }

    [Fact]
    public void LcdText_rejects_non_ascii_text()
    {
        // The LCD is a 7-bit ASCII strip; non-ASCII would corrupt the SysEx stream.
        // Use an escape so the test source itself stays ASCII-only (project convention).
        Assert.Throws<ArgumentException>(() => Push1Sysex.LcdText(line: 0, offset: 0, text: "caf\u00e9"));
    }

    [Fact]
    public void LcdText_rejects_text_that_overflows_the_68_column_line()
    {
        string tooLong = new string('A', 69);
        Assert.Throws<ArgumentException>(() => Push1Sysex.LcdText(line: 0, offset: 0, text: tooLong));
    }
}
