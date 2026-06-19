using Liveolator.Core.Settings;

namespace Liveolator.App.Theme;

public static class BuiltInUiThemes
{
    public const string PackageId = "liveolator.builtin.themes";
    public const string BrassworkId = "Brasswork";
    public const string SpartanId = "Spartan";

    public static IReadOnlyList<UiThemeDefinition> All { get; } =
    [
        // SPARTAN — the default look as a full theme, so "Apply" can switch BACK to it live and reset every
        // token (incl. control colours + clearing any background image). These values MUST mirror the
        // App.axaml defaults; SpartanMatchesAppDefaults (test) guards against drift.
        new UiThemeDefinition(
            SpartanId,
            "Spartan",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BgColor"] = "#0A0D13",
                ["S1Color"] = "#141A26",
                ["S2Color"] = "#0C1017",
                ["S3Color"] = "#1A2130",
                ["S4Color"] = "#26303F",
                ["HairColor"] = "#232B38",
                ["TextColor"] = "#E7ECF3",
                ["DimColor"] = "#8B95A7",
                ["FaintColor"] = "#5A6573",
                ["AccentColor"] = "#2F80F6",
                ["AccentLightColor"] = "#69A7FF",
                ["AccentDarkColor"] = "#1E5EC5",
                ["AccentWellColor"] = "#10294E",
                ["AccentInkColor"] = "#FFFFFF",
                ["RedColor"] = "#E5544A",
                ["GreenColor"] = "#2F80F6",
                ["AmberColor"] = "#2F80F6",
                ["VioletColor"] = "#2F80F6",
                ["MidiActiveColor"] = "#29C467",
                // Waveform = the VirtualDJ 3-band scheme (mirrors App.axaml; owner-requested, 2026-06-19):
                // red low/kick, green mid, blue/cyan high. WaveformColor is only the broadband fallback body.
                ["WaveformColor"] = "#2F80F6",
                ["KickColor"] = "#E23B2E",
                ["WaveMidColor"] = "#39C24A",
                ["WaveHighColor"] = "#A036A6E8",
                // Default control colours (match the App.axaml control-brush defaults).
                ["KnobArcColor"] = "#2F80F6",
                ["KnobTrackColor"] = "#26303F",
                ["KnobCapColor"] = "#0C1017",
                ["KnobPointerColor"] = "#E7ECF3",
                ["FaderFillColor"] = "#2F80F6",
                ["FaderTrackColor"] = "#26303F",
                ["FaderThumbColor"] = "#E7ECF3",
            }),
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
                // Waveform = the VirtualDJ 3-band scheme, consistent across themes (owner-requested,
                // 2026-06-19). WaveformColor (broadband fallback body) keeps the brass tint.
                ["WaveformColor"] = "#E89A18",
                ["KickColor"] = "#E23B2E",
                ["WaveMidColor"] = "#39C24A",
                ["WaveHighColor"] = "#A036A6E8",
            }),
    ];

    public static void Register(IUiThemeManager themes)
    {
        ArgumentNullException.ThrowIfNull(themes);
        themes.ReplacePackage(PackageId, All);
    }
}
