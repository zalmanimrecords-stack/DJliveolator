namespace Liveolator.Core.Automix;

/// <summary>
/// The auto-mix transition styles (doc 11 Auto-Mix): how the mixer is automated while the decks
/// blend. Each style is a pure automation profile over transition progress 0..1; all of them ride
/// the same shared beat clock and the same mixer actions a human uses.
/// </summary>
public enum AutomixStyle
{
    /// <summary>Crossfader only — the forgiving default; works with tempo sync alone (no beat grid).</summary>
    CrossFade,

    /// <summary>
    /// EQ blend with a downbeat-quantized bass swap — tops/mids blend first, the low band hands over
    /// in one beat at the midpoint. Requires a confident beat grid on both decks.
    /// </summary>
    EqMix,

    /// <summary>
    /// Filter-sweep exit — the outgoing deck thins out and lifts away on the single-knob high-pass
    /// while the incoming deck takes the floor. Uses only the existing per-deck filter (no FX engine).
    /// </summary>
    FxMix,
}
