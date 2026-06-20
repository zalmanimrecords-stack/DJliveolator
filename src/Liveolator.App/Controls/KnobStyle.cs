namespace Liveolator.App.Controls;

/// <summary>
/// The drawn shape of a <see cref="Knob"/>, selected per UI theme via the <c>KnobStyle</c> token.
/// Behaviour (drag / keys / two-way value) is identical across styles; only the rendering differs.
/// </summary>
public enum KnobStyle
{
    /// <summary>The default skeuomorphic rotary knob: track arc + value arc + domed cap + pointer.</summary>
    Rotary,

    /// <summary>An old guitar-amp "chicken-head" pointer knob: a rooster-head pointer on a low base,
    /// rotating to aim at the value. No domed cap. Used by the Retro Sci-Fi theme.</summary>
    ChickenHead,
}
