namespace Liveolator.Core.Beat;

/// <summary>
/// Where the active beat clock gets its tempo and phase. A single clock drives both the DJ mix
/// and the visual compositor regardless of source, so beat-synced visuals hold by construction
/// (doc 00/03).
/// </summary>
public enum BeatClockSource
{
    /// <summary>Driven by a playing deck's tempo/phase.</summary>
    Deck,

    /// <summary>Driven by realtime analysis of system/captured audio.</summary>
    System,

    /// <summary>Driven by a live input being analyzed.</summary>
    Input,

    /// <summary>Driven purely by performer tap/lock, no audio analysis.</summary>
    Manual,

    /// <summary>Driven by an external clock (Ableton Link / DJ-link).</summary>
    External,
}
