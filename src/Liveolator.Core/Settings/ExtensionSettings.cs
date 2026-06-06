namespace Liveolator.Core.Settings;

public sealed record ExtensionSettings
{
    public bool DeveloperMode { get; init; }
    public string? ActiveUiThemeId { get; init; }

    public static ExtensionSettings Default { get; } = new();

    public ExtensionSettings Normalized()
        => this with
        {
            ActiveUiThemeId = string.IsNullOrWhiteSpace(ActiveUiThemeId)
                ? null
                : ActiveUiThemeId.Trim(),
        };
}
