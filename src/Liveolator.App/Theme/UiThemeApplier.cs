using Avalonia;
using Avalonia.Media;
using Liveolator.Core.Settings;

namespace Liveolator.App.Theme;

public static class UiThemeApplier
{
    private static readonly IReadOnlyDictionary<string, string> BrushKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BgColor"] = "Bg",
            ["S1Color"] = "S1",
            ["S2Color"] = "S2",
            ["S3Color"] = "S3",
            ["S4Color"] = "S4",
            ["HairColor"] = "Hair",
            ["TextColor"] = "Text",
            ["DimColor"] = "Dim",
            ["FaintColor"] = "Faint",
            ["AccentColor"] = "Accent",
            ["AccentInkColor"] = "AccentInk",
            ["RedColor"] = "Red",
            ["GreenColor"] = "Green",
            ["AmberColor"] = "Amber",
            ["VioletColor"] = "Violet",
            ["MidiActiveColor"] = "MidiActive",
            ["WaveformColor"] = "Waveform",
            ["KickColor"] = "Kick",
        };

    public static void Apply(Application application, UiThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(theme);

        foreach ((string key, string value) in theme.Tokens)
        {
            if (key.EndsWith("Color", StringComparison.Ordinal))
            {
                Color color = Color.Parse(value);
                application.Resources[key] = color;
                if (BrushKeys.TryGetValue(key, out string? brushKey))
                    application.Resources[brushKey] = new SolidColorBrush(color);
                if (key == "WaveformColor")
                    application.Resources["WaveformAhead"] = new SolidColorBrush(color, 0.5);
            }
            else if (key.EndsWith("FontFamily", StringComparison.Ordinal))
            {
                application.Resources[key] = new FontFamily(value);
            }
            else if (double.TryParse(
                         value,
                         System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture,
                         out double number))
            {
                application.Resources[key] = number;
            }
        }

        ApplyFluentAccent(application, theme.Tokens);
    }

    private static void ApplyFluentAccent(
        Application application,
        IReadOnlyDictionary<string, string> tokens)
    {
        if (!tokens.TryGetValue("AccentColor", out string? accentValue))
            return;

        Color accent = Color.Parse(accentValue);
        Color light = tokens.TryGetValue("AccentLightColor", out string? lightValue)
            ? Color.Parse(lightValue)
            : accent;
        Color dark = tokens.TryGetValue("AccentDarkColor", out string? darkValue)
            ? Color.Parse(darkValue)
            : accent;

        application.Resources["SystemAccentColor"] = accent;
        application.Resources["SystemAccentColorDark1"] = dark;
        application.Resources["SystemAccentColorDark2"] = dark;
        application.Resources["SystemAccentColorDark3"] = dark;
        application.Resources["SystemAccentColorLight1"] = light;
        application.Resources["SystemAccentColorLight2"] = light;
        application.Resources["SystemAccentColorLight3"] = light;
    }
}
