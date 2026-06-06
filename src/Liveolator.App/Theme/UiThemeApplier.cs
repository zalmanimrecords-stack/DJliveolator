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
    }
}
