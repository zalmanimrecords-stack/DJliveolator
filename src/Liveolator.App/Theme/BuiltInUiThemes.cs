using Liveolator.Core.Settings;

namespace Liveolator.App.Theme;

public static class BuiltInUiThemes
{
    public const string PackageId = "liveolator.builtin.themes";
    public const string BrassworkId = "Brasswork";
    public const string RetroSciFiId = "RetroSciFi";
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
                // Waveform = the 3-band scheme (mirrors App.axaml; owner-requested, 2026-06-19):
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
                ["PanelRadius"] = "16",
                ["ControlRadius"] = "3",
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
                // Waveform = the 3-band scheme, consistent across themes (owner-requested,
                // 2026-06-19). WaveformColor (broadband fallback body) keeps the brass tint.
                ["WaveformColor"] = "#E89A18",
                ["KickColor"] = "#E23B2E",
                ["WaveMidColor"] = "#39C24A",
                ["WaveHighColor"] = "#A036A6E8",
                ["KnobArcColor"] = "#D78A16",
                ["KnobTrackColor"] = "#4A2C0A",
                ["KnobCapColor"] = "#0B0803",
                ["KnobPointerColor"] = "#F1D38B",
                ["FaderFillColor"] = "#D78A16",
                ["FaderTrackColor"] = "#4A2C0A",
                ["FaderThumbColor"] = "#F1D38B",
                ["PanelRadius"] = "10",
                ["ControlRadius"] = "3",
            }),
        new UiThemeDefinition(
            RetroSciFiId,
            "Retro Sci-Fi",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BgColor"] = "#07080A",
                ["S1Color"] = "#11181A",
                ["S2Color"] = "#090D0F",
                ["S3Color"] = "#182224",
                ["S4Color"] = "#243236",
                ["HairColor"] = "#4E5B52",
                ["TextColor"] = "#E8F4DF",
                ["DimColor"] = "#A7B894",
                ["FaintColor"] = "#657263",
                ["AccentColor"] = "#E2F05A",
                ["AccentLightColor"] = "#FAFF9A",
                ["AccentDarkColor"] = "#9EAE24",
                ["AccentWellColor"] = "#262A0D",
                ["AccentInkColor"] = "#101107",
                ["RedColor"] = "#F05A3E",
                ["GreenColor"] = "#7BD66F",
                ["AmberColor"] = "#E2F05A",
                ["VioletColor"] = "#D47AE8",
                ["MidiActiveColor"] = "#7BD66F",
                ["WaveformColor"] = "#E2F05A",
                ["KickColor"] = "#F05A3E",
                ["WaveMidColor"] = "#7BD66F",
                ["WaveHighColor"] = "#A0627DF8",
                // Knobs use the vintage cream-bakelite amp look (see KnobStyle): cream cap, sepia engraved
                // ticks/numbers, tan dial plate, brass pointer — a warm contrast against the dark face.
                ["KnobArcColor"] = "#8A7350",
                ["KnobTrackColor"] = "#D7C7A2",
                ["KnobCapColor"] = "#ECE2C8",
                ["KnobPointerColor"] = "#B2935E",
                ["FaderFillColor"] = "#E2F05A",
                ["FaderTrackColor"] = "#243236",
                ["FaderThumbColor"] = "#E8F4DF",
                ["PanelRadius"] = "2",
                ["ControlRadius"] = "0",
                // Vintage cream-bakelite scalloped amp-dial knobs, unique to this theme.
                ["KnobStyle"] = "ScallopedDial",
            }),
    ];

    public static void Register(IUiThemeManager themes)
    {
        ArgumentNullException.ThrowIfNull(themes);
        themes.ReplacePackage(PackageId, All);
    }
}
