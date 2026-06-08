using Liveolator.Core.Settings;

namespace Liveolator.App.Theme;

public static class BuiltInUiThemes
{
    public const string PackageId = "liveolator.builtin.themes";
    public const string BrassworkId = "Brasswork";

    public static IReadOnlyList<UiThemeDefinition> All { get; } =
    [
        new UiThemeDefinition(
            BrassworkId,
            "Brasswork",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BgColor"] = "#080603",
                ["S1Color"] = "#171006",
                ["S2Color"] = "#0B0803",
                ["S3Color"] = "#241707",
                ["S4Color"] = "#4A2C0A",
                ["HairColor"] = "#6F4715",
                ["TextColor"] = "#F1D38B",
                ["DimColor"] = "#B58A43",
                ["FaintColor"] = "#735525",
                ["AccentColor"] = "#D78A16",
                ["AccentLightColor"] = "#F0B84E",
                ["AccentDarkColor"] = "#7A4308",
                ["AccentWellColor"] = "#2B1704",
                ["AccentInkColor"] = "#180D02",
                ["RedColor"] = "#C74420",
                ["GreenColor"] = "#78B34D",
                ["AmberColor"] = "#D78A16",
                ["VioletColor"] = "#9E6A9A",
                ["MidiActiveColor"] = "#78B34D",
                ["WaveformColor"] = "#E89A18",
                ["KickColor"] = "#7DBA50",
            }),
    ];

    public static void Register(IUiThemeManager themes)
    {
        ArgumentNullException.ThrowIfNull(themes);
        themes.ReplacePackage(PackageId, All);
    }
}
