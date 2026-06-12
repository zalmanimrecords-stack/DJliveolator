using Liveolator.Core.Settings;

namespace Liveolator.App.Theme;

public static class BuiltInUiThemes
{
    public const string PackageId = "liveolator.builtin.themes";
    public const string BrassworkId = "Brasswork";
    public const string AnalogId = "Analog";
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
                ["WaveformColor"] = "#F0C23C",
                ["KickColor"] = "#27C56A",
                // Default control colours (match the App.axaml control-brush defaults).
                ["KnobArcColor"] = "#2F80F6",
                ["KnobTrackColor"] = "#26303F",
                ["KnobCapColor"] = "#0C1017",
                ["KnobPointerColor"] = "#E7ECF3",
                ["FaderFillColor"] = "#2F80F6",
                ["FaderTrackColor"] = "#26303F",
                ["FaderThumbColor"] = "#E7ECF3",
            }),
        // ANALOG (doc 30): a vintage-synth look — warm wood panels, an amber signal accent, and a
        // chrome+wood texture behind the shell (BackgroundImage). The knob/fader colour tokens give the
        // controls an ivory cap + amber arc without touching the surface/text tokens.
        new UiThemeDefinition(
            AnalogId,
            "Analog",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BgColor"] = "#1B120A",
                ["S1Color"] = "#2A1E12",
                ["S2Color"] = "#160F08",
                ["S3Color"] = "#332413",
                ["S4Color"] = "#4A3520",
                ["HairColor"] = "#5C4632",
                ["TextColor"] = "#EFE2C8",
                ["DimColor"] = "#BCA985",
                ["FaintColor"] = "#8C7A5C",
                ["AccentColor"] = "#E0922A",
                ["AccentLightColor"] = "#F3B85A",
                ["AccentDarkColor"] = "#9C5E12",
                ["AccentWellColor"] = "#2A1B0A",
                ["AccentInkColor"] = "#1A1206",
                ["RedColor"] = "#C24A2E",
                ["GreenColor"] = "#8FB24D",
                ["AmberColor"] = "#E0922A",
                ["VioletColor"] = "#B07A4A",
                ["MidiActiveColor"] = "#79B34D",
                ["WaveformColor"] = "#E0A22A",
                ["KickColor"] = "#8FB24D",
                // Vintage knob + fader: ivory cap, dark engraved pointer, amber arc, wood-toned track.
                ["KnobCapColor"] = "#E8DCC2",
                ["KnobPointerColor"] = "#2A1C0E",
                ["KnobArcColor"] = "#E0922A",
                ["KnobTrackColor"] = "#3A2A18",
                ["FaderFillColor"] = "#E0922A",
                ["FaderThumbColor"] = "#E8DCC2",
                ["FaderTrackColor"] = "#3A2A18",
                ["BackgroundImage"] = "avares://Liveolator.App/Assets/Themes/analog/background.png",
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
