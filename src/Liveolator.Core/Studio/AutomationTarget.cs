namespace Liveolator.Core.Studio;

/// <summary>
/// What a STUDIO automation lane controls on its deck over time. Each maps to an existing
/// per-deck <c>PerformanceAction</c> when the arrangement is played or rendered (the timeline is
/// just another action source — doc 04): gain/EQ/filter via the mixer, pitch via the deck.
/// </summary>
public enum AutomationTarget
{
    /// <summary>Per-deck channel volume (0..1) — <c>MixerChannelGain</c>.</summary>
    DeckGain,

    /// <summary>Low EQ band (0..1, 0.5 = flat) — <c>MixerEqBand</c> Low.</summary>
    EqLow,

    /// <summary>Mid EQ band (0..1, 0.5 = flat) — <c>MixerEqBand</c> Mid.</summary>
    EqMid,

    /// <summary>High EQ band (0..1, 0.5 = flat) — <c>MixerEqBand</c> High.</summary>
    EqHigh,

    /// <summary>Single-knob filter (0..1, 0.5 = off) — <c>MixerFilter</c>.</summary>
    Filter,

    /// <summary>Pitch / tempo fader position (0..1, 0.5 = no change) — <c>DeckPitch</c>.</summary>
    Pitch,
}
