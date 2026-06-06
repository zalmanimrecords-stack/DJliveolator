namespace Liveolator.Core.Mixer;

/// <summary>
/// Immutable state of the headphone-cue (PFL — pre-fade listen) bus (doc 11). Independent of the
/// crossfader and master: a DJ pre-listens an incoming deck in the headphones while the master plays
/// to the house. Which decks feed the bus lives per-deck on <see cref="DeckChannelState.CueEnabled"/>;
/// this record holds the two bus-level controls — the headphone <see cref="Level"/> and the
/// cue/master <see cref="Mix"/> blend knob. Pure data; the audible gains derive in <see cref="CueMixMath"/>.
/// </summary>
/// <param name="Level">Headphone-cue output level, 0..1 (1 = full). Scales the whole cue output.</param>
/// <param name="Mix">Cue/master blend, 0..1: 0 = only the cued (PFL) decks in the headphones,
/// 1 = only the master mix, 0.5 = an equal-power blend of both — the classic "cue mix" knob.</param>
public sealed record CueBusState(double Level, double Mix)
{
    /// <summary>Blend knob position at which the headphones carry only the cued (PFL) decks.</summary>
    public const double FullCue = 0.0;

    /// <summary>Blend knob position at which the headphones carry only the master mix.</summary>
    public const double FullMaster = 1.0;

    /// <summary>Cue bus at full level, blended fully to the cued decks (PFL) — the DJ default.</summary>
    public static CueBusState Default { get; } = new(Level: 1.0, Mix: FullCue);

    /// <summary>Returns a copy with the headphone level clamped to 0..1.</summary>
    public CueBusState WithLevel(double level)
        => this with { Level = Math.Clamp(level, 0.0, 1.0) };

    /// <summary>Returns a copy with the cue/master blend clamped to 0..1.</summary>
    public CueBusState WithMix(double mix)
        => this with { Mix = Math.Clamp(mix, 0.0, 1.0) };
}
