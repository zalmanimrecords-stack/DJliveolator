using Liveolator.Core.Mapping;
using RtMidi.Core.Enums;
using RtMidi.Core.Messages;

namespace Liveolator.Midi;

/// <summary>
/// Pure translation between RtMidi.Core's message structs and the library-agnostic Core
/// <see cref="MidiMessage"/> (doc 05). This is the only place that knows both vocabularies, kept
/// free of any device/native calls so it unit-tests with no MIDI hardware present — the same
/// isolation principle the Audio binding applies to BASS. RtMidi message structs are plain managed
/// values; constructing/reading them does not load the native rtmidi library.
/// </summary>
internal static class RtMidiMessageTranslator
{
    private const int SevenBitMask = 0x7F;

    public static MidiMessage FromNoteOn(in NoteOnMessage msg)
        // A note-on with velocity 0 is a note-off on the wire; Core's BindingMatcher already treats
        // NoteOn vel0 as NoteOff, so we preserve the raw shape and let Core decide.
        => new(MidiMessageType.NoteOn, (int)msg.Channel, (int)msg.Key, msg.Velocity);

    public static MidiMessage FromNoteOff(in NoteOffMessage msg)
        => new(MidiMessageType.NoteOff, (int)msg.Channel, (int)msg.Key, msg.Velocity);

    public static MidiMessage FromControlChange(in ControlChangeMessage msg)
        => new(MidiMessageType.ControlChange, (int)msg.Channel, msg.Control, msg.Value);

    public static MidiMessage FromPitchBend(in PitchBendMessage msg)
    {
        // RtMidi exposes a single 14-bit value (0..16383, center 8192). Core's MidiMessage carries
        // the raw 7-bit LSB/MSB (doc 05) so ControlValueConverter reconstructs it like a wire event.
        int value14 = msg.Value;
        int lsb = value14 & SevenBitMask;
        int msb = (value14 >> 7) & SevenBitMask;
        return new MidiMessage(MidiMessageType.PitchBend, (int)msg.Channel, lsb, msb);
    }
}
