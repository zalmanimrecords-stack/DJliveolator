namespace Liveolator.Core.Mapping;

/// <summary>
/// Shapes how a raw 0..1 absolute control value maps to the action value, so a fader can feel
/// linear, or weighted toward its low or high end (doc 05).
/// </summary>
public enum ValueCurve
{
    /// <summary>Pass-through: output equals input.</summary>
    Linear,

    /// <summary>Output = input², giving finer resolution near the low end.</summary>
    Exponential,

    /// <summary>Output = √input, giving finer resolution near the high end.</summary>
    Logarithmic,
}
