namespace Liveolator.Core.Actions;

/// <summary>
/// How the control bound to an action expresses its value, so one action <see cref="PerformanceActionKind"/>
/// can be driven by a button, a toggle, a fader, or an encoder without the handler caring which.
/// </summary>
public enum ActionInputMode
{
    /// <summary>Button held down then released (e.g. a pad press); value is ignored.</summary>
    Momentary,

    /// <summary>On/off latch toggled by each trigger (e.g. a lock button); value is ignored.</summary>
    Toggle,

    /// <summary>An absolute position in 0..1 (e.g. a fader or knob).</summary>
    Absolute,

    /// <summary>A signed delta from an endless encoder (e.g. +0.01 per tick).</summary>
    Relative,
}
