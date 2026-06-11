namespace Liveolator.Core.Skins;

/// <summary>The control types a <see cref="ControlSkinFile"/> can style. Matched case-insensitively.</summary>
public static class ControlSkinKind
{
    public const string Knob = "Knob";
    public const string Slider = "Slider";

    public static readonly IReadOnlyList<string> All = new[] { Knob, Slider };

    public static bool IsKnown(string? kind)
        => All.Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));
}
