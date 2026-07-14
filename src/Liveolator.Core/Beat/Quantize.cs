namespace Liveolator.Core.Beat;

/// <summary>
/// When a quantized action should fire relative to the beat grid. Used by visual clip launches and
/// playlist actions so audio and visual changes land on the same grid (doc 03/04).
/// </summary>
public enum Quantize
{
    /// <summary>Fire immediately, ignoring the grid.</summary>
    Immediate,

    /// <summary>Fire on the next beat boundary.</summary>
    NextBeat,

    /// <summary>Fire on the next bar boundary.</summary>
    NextBar,

    /// <summary>Fire on the next boundary of every N bars (phrase-aligned).</summary>
    EveryNBars,
}
