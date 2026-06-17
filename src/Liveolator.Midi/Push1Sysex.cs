using System;
using Liveolator.Core.Mapping;

namespace Liveolator.Midi;

/// <summary>
/// Pure byte formatting for Ableton Push 1 feedback (doc 06). Push 1 LED control is mostly plain MIDI:
/// pad LEDs are NoteOn (velocity = color palette index, channel = animation) and button LEDs are CC;
/// only the LCD strip and the User-mode switch use SysEx. These helpers build the messages/byte arrays
/// only  -  the actual device send goes through the existing <see cref="IMidiOutput"/> path
/// (<c>Send</c> / <c>SendSysEx</c>), which is manual-verified against real hardware.
/// </summary>
/// <remarks>
/// The note/CC/SysEx layout follows Ableton's published Push 1 model. It is isolated here so all
/// device-specific bytes live in one file (global standards #2/#3); a Push 2/3 RGB-SysEx adapter would
/// be a sibling. Push 1 must be in User mode for these to take effect (doc 06).
/// </remarks>
public static class Push1Sysex
{
    /// <summary>Lowest pad note in the 8x8 grid; bottom-left pad. Pad index N -> note 36 + N.</summary>
    public const int PadBaseNote = 36;

    /// <summary>Pads in the 8x8 grid (indices 0..63).</summary>
    public const int PadCount = 64;

    /// <summary>Highest legal 7-bit MIDI data value (color index, CC value, etc.).</summary>
    private const int MaxSevenBit = 127;

    /// <summary>LCD rows on the Push 1 display strip (four 68-char lines).</summary>
    private const int LcdLineCount = 4;

    /// <summary>Characters per LCD line.</summary>
    private const int LcdLineLength = 68;

    /// <summary>Highest ASCII code the 7-bit LCD strip accepts.</summary>
    private const int MaxAscii = 127;

    // Ableton/Akai SysEx header for Push 1: F0, manufacturer 0x47 (Akai), device 0x7F, model 0x15.
    private static readonly byte[] SysExHeader = { 0xF0, 0x47, 0x7F, 0x15 };
    private const byte SysExEnd = 0xF7;

    // Set-mode command: 0x62, fixed length 00 01, then the mode byte (01 = User, 00 = Live).
    private const byte SetModeCommand = 0x62;

    // LCD write command base: 0x18 + line index selects which of the four display rows to write.
    private const byte LcdWriteCommandBase = 0x18;

    /// <summary>
    /// Formats a pad-LED update as a NoteOn: <paramref name="color"/> is the velocity (Push 1 fixed
    /// 0..127 color palette) and <paramref name="blink"/> selects the animation via the channel.
    /// </summary>
    /// <param name="pad">Pad index 0..63 (bottom-left = 0).</param>
    /// <param name="color">Color palette index 0..127.</param>
    /// <param name="blink">Pad animation (solid or a blink/pulse rate).</param>
    public static MidiMessage PadLed(int pad, int color, Push1PadAnimation blink)
    {
        if (pad is < 0 or >= PadCount)
            throw new ArgumentOutOfRangeException(nameof(pad), pad, $"Pad index must be 0..{PadCount - 1}.");
        if (color is < 0 or > MaxSevenBit)
            throw new ArgumentOutOfRangeException(nameof(color), color, $"Color index must be 0..{MaxSevenBit}.");

        return new MidiMessage(MidiMessageType.NoteOn, AnimationChannel(blink), PadBaseNote + pad, color);
    }

    /// <summary>
    /// Formats a button-LED update as a Control Change on the Push channel. White/limited-color buttons
    /// accept a 7-bit brightness/color <paramref name="value"/> (0 = off).
    /// </summary>
    /// <param name="cc">The button's CC number.</param>
    /// <param name="value">LED value 0..127 (0 = off).</param>
    public static MidiMessage ButtonLed(int cc, int value)
    {
        if (cc is < 0 or > MaxSevenBit)
            throw new ArgumentOutOfRangeException(nameof(cc), cc, $"CC number must be 0..{MaxSevenBit}.");
        if (value is < 0 or > MaxSevenBit)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"LED value must be 0..{MaxSevenBit}.");

        // Push 1 sends/receives button LEDs on channel 0 (User mode).
        return new MidiMessage(MidiMessageType.ControlChange, 0, cc, value);
    }

    /// <summary>
    /// Builds the SysEx frame that switches the Push between User mode (raw MIDI to the app) and Live
    /// mode. Returns the full <c>F0 47 7F 15 62 00 01 &lt;mode&gt; F7</c> frame.
    /// </summary>
    /// <param name="userMode">True for User mode (01), false for Live mode (00).</param>
    public static byte[] SetUserMode(bool userMode)
    {
        var frame = new byte[SysExHeader.Length + 5];
        int i = 0;
        Array.Copy(SysExHeader, frame, SysExHeader.Length);
        i += SysExHeader.Length;
        frame[i++] = SetModeCommand;
        frame[i++] = 0x00; // length MSB (fixed)
        frame[i++] = 0x01; // length LSB (fixed): one mode byte follows
        frame[i++] = (byte)(userMode ? 0x01 : 0x00);
        frame[i] = SysExEnd;
        return frame;
    }

    /// <summary>
    /// Builds the SysEx frame that writes ASCII <paramref name="text"/> to one LCD display line at a
    /// column offset. Returns the full <c>F0 47 7F 15 &lt;0x18+line&gt; 00 &lt;len&gt; &lt;offset&gt;
    /// &lt;ascii...&gt; F7</c> frame, where <c>len</c> counts the offset byte plus the characters.
    /// </summary>
    /// <param name="line">Display line 0..3.</param>
    /// <param name="offset">Column offset 0..67 where the text starts.</param>
    /// <param name="text">ASCII text; must fit within the 68-column line from <paramref name="offset"/>.</param>
    public static byte[] LcdText(int line, int offset, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (line is < 0 or >= LcdLineCount)
            throw new ArgumentOutOfRangeException(nameof(line), line, $"LCD line must be 0..{LcdLineCount - 1}.");
        if (offset is < 0 or >= LcdLineLength)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, $"Offset must be 0..{LcdLineLength - 1}.");
        if (offset + text.Length > LcdLineLength)
            throw new ArgumentException(
                $"Text of length {text.Length} at offset {offset} overflows the {LcdLineLength}-column line.",
                nameof(text));

        byte[] ascii = EncodeAscii(text);

        var frame = new byte[SysExHeader.Length + 4 + ascii.Length + 1];
        int i = 0;
        Array.Copy(SysExHeader, frame, SysExHeader.Length);
        i += SysExHeader.Length;
        frame[i++] = (byte)(LcdWriteCommandBase + line);
        frame[i++] = 0x00;                          // reserved
        frame[i++] = (byte)(ascii.Length + 1);      // length: offset byte + characters
        frame[i++] = (byte)offset;                  // column offset
        Array.Copy(ascii, 0, frame, i, ascii.Length);
        i += ascii.Length;
        frame[i] = SysExEnd;
        return frame;
    }

    private static byte[] EncodeAscii(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c > MaxAscii)
                throw new ArgumentException(
                    $"LCD text must be 7-bit ASCII; character '{c}' (U+{(int)c:X4}) is out of range.", nameof(text));
            bytes[i] = (byte)c;
        }
        return bytes;
    }

    // Push 1 animation lives on the NoteOn channel: channel 0 = solid, higher channels = blink/pulse.
    private static int AnimationChannel(Push1PadAnimation blink) => blink switch
    {
        Push1PadAnimation.Solid => 0,
        Push1PadAnimation.BlinkSlow => 1,
        Push1PadAnimation.BlinkFast => 2,
        Push1PadAnimation.Pulse => 7,
        _ => 0,
    };
}
