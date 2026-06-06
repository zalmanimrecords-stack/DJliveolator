namespace Liveolator.Core.Settings;

public sealed record UiThemeDefinition(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Tokens);

public sealed record UiThemeValidationResult(
    bool IsValid,
    UiThemeDefinition? Theme,
    IReadOnlyList<string> Errors);

public interface IUiThemeManager
{
    IReadOnlyList<UiThemeDefinition> AvailableThemes { get; }
    UiThemeValidationResult Validate(UiThemeDefinition theme);
    bool TryGet(string id, out UiThemeDefinition theme);
    void ReplacePackage(string packageId, IEnumerable<UiThemeDefinition> themes);
    void RemovePackage(string packageId);
}
