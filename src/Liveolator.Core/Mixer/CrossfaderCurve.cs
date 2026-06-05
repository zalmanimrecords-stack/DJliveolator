namespace Liveolator.Core.Mixer;

/// <summary>
/// Shape of the crossfader's A→B gain transition (doc 11). A DJ picks the curve to suit the mix:
/// a long blend vs. a fast cut. The curve only affects how the crossfader position maps to the two
/// deck gains; it never changes per-deck gain/EQ/filter.
/// </summary>
public enum CrossfaderCurve
{
    /// <summary>Constant-power blend (default): both decks audible across the middle, no dip.</summary>
    Smooth,

    /// <summary>Equal-gain (linear) blend: simple straight-line fade between the decks.</summary>
    Linear,

    /// <summary>Fast cut: a deck stays near full until the fader is close to the opposite side
    /// (scratch/cut style).</summary>
    Sharp,
}
