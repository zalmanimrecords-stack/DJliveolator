namespace Liveolator.Core.Studio;

/// <summary>
/// How one planned set entry hands over to the next (the STUDIO pre-show planner, doc dj.studio
/// model). Distinct from <see cref="Mixer.CrossfaderCurve"/>, which only shapes the fader taper:
/// the <see cref="StudioTransition.Kind"/> decides <em>what</em> happens, the curve decides
/// <em>how</em> the crossfade tapers.
/// </summary>
public enum TransitionKind
{
    /// <summary>Hard switch on the boundary — no overlap (used when tempo/key are unknown).</summary>
    Cut,

    /// <summary>Overlapping blend driven by the crossfader across the transition length.</summary>
    Crossfade,

    /// <summary>Crossfade with a bass swap: the incoming deck's lows come up only once the
    /// outgoing deck's lows are pulled, avoiding a muddy double-bass through the blend.</summary>
    BassSwap,
}
