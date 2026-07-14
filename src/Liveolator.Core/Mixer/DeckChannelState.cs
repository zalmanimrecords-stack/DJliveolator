namespace Liveolator.Core.Mixer;

/// <summary>
/// Immutable per-deck channel-strip state on the software mixer (doc 11): the deck's own gain
/// (pre-crossfader), its 3-band EQ, a single-knob filter, and whether it is routed to the headphone
/// cue (PFL) bus. One of these exists per deck slot in <see cref="MixerState"/>. All controls are
/// normalized 0..1 to match the action/fader convention and serialize cleanly (doc 13).
/// </summary>
/// <param name="Gain">Channel gain, 0..1 (1 = unity/full).</param>
/// <param name="Eq">3-band EQ for this deck.</param>
/// <param name="Filter">Single-knob filter, 0..1 where <see cref="FilterCenter"/> (0.5) is off,
/// below center is a low-pass sweep and above center is a high-pass sweep.</param>
/// <param name="CueEnabled">True when this deck feeds the headphone cue (PFL) bus.</param>
public sealed record DeckChannelState(
    double Gain,
    EqBands Eq,
    double Filter,
    bool CueEnabled)
{
    /// <summary>The filter knob position at which the filter is bypassed (no effect).</summary>
    public const double FilterCenter = 0.5;

    /// <summary>A freshly loaded deck: unity gain, flat EQ, filter off, not cued.</summary>
    public static DeckChannelState Default { get; } =
        new(Gain: 1.0, Eq: EqBands.Flat, Filter: FilterCenter, CueEnabled: false);
}
