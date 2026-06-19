namespace Liveolator.Core.Mixer;

/// <summary>
/// Global EQ cut-depth mode (doc 11): how deep the cut half of every band's knob travel is allowed
/// to attenuate. Cutting always sweeps from 0 dB at unity (<see cref="EqBands.Unity"/>) down to the
/// mode's floor at the bottom of the knob; the boost half and the band Q are unaffected. This is the
/// "EQ vs. isolator depth" control real club mixers expose (Rane MP2015 switchable kill depth, the
/// Xone EQ-vs-filter split): <see cref="Kill"/> reaches silence at the bottom (the classic DJ kill,
/// and Liveolator's historical default), while gentler modes floor the cut so a band can be shaped
/// without ever fully removing it. The mode is mixer-wide, not per deck.
/// </summary>
public enum EqCutMode
{
    /// <summary>Gentle, musical EQ: the cut floors at -12 dB — a band can be tamed but never killed.</summary>
    Eq = 0,

    /// <summary>Deep cut: the cut floors at -24 dB, still short of a full kill.</summary>
    Deep = 1,

    /// <summary>Full kill: the cut reaches silence at the bottom of the knob. The default, matching a
    /// hardware DJ mixer's band-kill and Liveolator's original EQ behaviour.</summary>
    Kill = 2,
}

/// <summary>Cut-depth metadata for each <see cref="EqCutMode"/> — kept beside the enum so the DSP
/// (<see cref="MixerMath"/>) and the UI label read the same source of truth.</summary>
public static class EqCutModeExtensions
{
    /// <summary>The maximum cut, in (negative) decibels, the cut half of a band's knob maps to at the
    /// bottom of its travel. <see cref="EqCutMode.Kill"/> uses the deep range for the sweep but snaps
    /// to a true kill at the very bottom (see <see cref="IsFullKill"/>).</summary>
    public static double MaxCutDb(this EqCutMode mode) => mode switch
    {
        EqCutMode.Eq => 12.0,
        EqCutMode.Deep => 24.0,
        EqCutMode.Kill => 24.0,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown EQ cut mode."),
    };

    /// <summary>True when the cut reaches full silence at the bottom of the knob (Kill only).</summary>
    public static bool IsFullKill(this EqCutMode mode) => mode == EqCutMode.Kill;

    /// <summary>Short uppercase label for a button face (e.g. "KILL", "DEEP", "EQ").</summary>
    public static string Label(this EqCutMode mode) => mode switch
    {
        EqCutMode.Eq => "EQ",
        EqCutMode.Deep => "DEEP",
        EqCutMode.Kill => "KILL",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown EQ cut mode."),
    };

    /// <summary>The next mode in the cycle, getting progressively coarser: EQ → DEEP → KILL → EQ.</summary>
    public static EqCutMode Next(this EqCutMode mode) => mode switch
    {
        EqCutMode.Eq => EqCutMode.Deep,
        EqCutMode.Deep => EqCutMode.Kill,
        EqCutMode.Kill => EqCutMode.Eq,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown EQ cut mode."),
    };
}
