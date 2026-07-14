namespace Liveolator.Midi;

/// <summary>
/// Push 1 pad-LED animation, selected by the MIDI channel of the pad's NoteOn (doc 06): channel 0 =
/// solid, higher channels = blink/pulse at fixed rates. The color itself is the NoteOn velocity.
/// </summary>
public enum Push1PadAnimation
{
    /// <summary>Steady, no animation (NoteOn channel 0).</summary>
    Solid,

    /// <summary>Slow blink.</summary>
    BlinkSlow,

    /// <summary>Fast blink.</summary>
    BlinkFast,

    /// <summary>Pulse / fade in-out.</summary>
    Pulse,
}
