namespace Liveolator.App.Controls;

/// <summary>
/// The drawn shape of a <see cref="Knob"/>, selected per UI theme via the <c>KnobStyle</c> token.
/// Behaviour (drag / keys / two-way value) is identical across styles; only the rendering differs.
/// </summary>
public enum KnobStyle
{
    /// <summary>The default skeuomorphic rotary knob: track arc + value arc + domed cap + pointer.</summary>
    Rotary,

    /// <summary>A vintage cream-bakelite amp knob: a scalloped (piecrust-edged) glossy cap on a numbered
    /// dial plate with engraved sepia ticks and a brass pointer. Used by the Retro Sci-Fi theme.</summary>
    ScallopedDial,
}
