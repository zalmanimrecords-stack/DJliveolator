namespace Liveolator.Core.Mapping;

/// <summary>
/// A normalized inbound MIDI event, decoupled from any MIDI library. The binding project adapts
/// the concrete library's events into this shape; Core mapping logic depends only on this record
/// so it unit-tests without any device (doc 05).
/// </summary>
/// <param name="Type">The message shape.</param>
/// <param name="Channel">MIDI channel, 0..15.</param>
/// <param name="Data1">Note or CC number; for pitch bend, the 7-bit LSB.</param>
/// <param name="Data2">Velocity or CC value; for pitch bend, the 7-bit MSB.</param>
public sealed record MidiMessage(MidiMessageType Type, int Channel, int Data1, int Data2);
