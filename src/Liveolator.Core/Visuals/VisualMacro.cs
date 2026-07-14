namespace Liveolator.Core.Visuals;

/// <summary>
/// A named continuous parameter (intensity, speed, echo, kaleidoscope, …) driven by Push knobs, UI
/// sliders, or autopilot, mapped to a concrete layer/effect parameter via <see cref="MacroTarget"/>
/// (doc 08). Controls supply a normalized 0..1 value; <see cref="Resolve"/> maps it to the target's
/// real range.
/// </summary>
public sealed record VisualMacro
{
    public VisualMacro(string name, double min, double max, double @default, MacroTarget target)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        if (max < min)
            throw new ArgumentException("Macro max must be >= min.", nameof(max));
        if (@default < min || @default > max)
            throw new ArgumentOutOfRangeException(nameof(@default), @default, "Default must be within [min, max].");

        Min = min;
        Max = max;
        Default = @default;
    }

    public string Name { get; init; }

    public double Min { get; init; }

    public double Max { get; init; }

    public double Default { get; init; }

    public MacroTarget Target { get; init; }

    /// <summary>Maps a normalized 0..1 control value to the macro's [Min, Max] range (input clamped).</summary>
    public double Resolve(double normalized)
    {
        double clamped = Math.Clamp(normalized, 0.0, 1.0);
        return Min + clamped * (Max - Min);
    }
}
