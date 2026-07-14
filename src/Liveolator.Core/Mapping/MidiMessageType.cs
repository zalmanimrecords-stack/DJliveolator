namespace Liveolator.Core.Mapping;

/// <summary>
/// The MIDI message shapes the mapping engine reacts to. Kept library-agnostic so the concrete
/// MIDI binding (doc 05) can be swapped without touching mapping logic.
/// </summary>
public enum MidiMessageType
{
    NoteOn,
    NoteOff,
    ControlChange,
    PitchBend,
}
