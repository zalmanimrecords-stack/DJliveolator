namespace Liveolator.Core.Settings;

public sealed record ExtensionSettings
{
    public bool DeveloperMode { get; init; }
    public string? ActiveUiThemeId { get; init; }

    /// <summary>Id of the active control skin for rotary knobs (doc 30), or null for the built-in look.</summary>
    public string? ActiveKnobSkinId { get; init; }

    /// <summary>Id of the active control skin for sliders/faders (doc 30), or null for the built-in look.</summary>
    public string? ActiveSliderSkinId { get; init; }

    public static ExtensionSettings Default { get; } = new();

    public ExtensionSettings Normalized()
        => this with
        {
            ActiveUiThemeId = Trimmed(ActiveUiThemeId),
            ActiveKnobSkinId = Trimmed(ActiveKnobSkinId),
            ActiveSliderSkinId = Trimmed(ActiveSliderSkinId),
        };

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
