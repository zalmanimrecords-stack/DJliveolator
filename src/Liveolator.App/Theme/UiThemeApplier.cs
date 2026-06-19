using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
            ["WaveMidColor"] = "WaveMid",
            ["WaveHighColor"] = "WaveHigh",
            // Per-control colours (doc 30): override the brush resources the Knob/Fader styles bind to.
            ["KnobArcColor"] = "KnobArc",
            ["KnobTrackColor"] = "KnobTrack",
            ["KnobCapColor"] = "KnobCap",
            ["KnobPointerColor"] = "KnobPointer",
            ["FaderFillColor"] = "FaderFill",
            ["FaderTrackColor"] = "FaderTrack",
            ["FaderThumbColor"] = "FaderThumb",
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
                // Radius tokens feed a CornerRadius property. Store them as CornerRadius so style
                // re-evaluation (which casts the resource directly, without the Double->CornerRadius
                // conversion the first apply uses) does not throw InvalidCastException and crash the app.
                application.Resources[key] = key.EndsWith("Radius", StringComparison.Ordinal)
                    ? new CornerRadius(number)
                    : number;
            }
        }

        ApplyFluentAccent(application, theme.Tokens);
        ApplyBackground(application, theme.Tokens);
    }

    // The app (main-window) background: a theme may replace the solid Bg with a texture image (doc 30 —
    // e.g. the Analog chrome+wood look). A theme without a BackgroundImage resets AppBackground to the
    // solid Bg just applied, so switching back to a flat theme clears any previous image.
    private static void ApplyBackground(Application application, IReadOnlyDictionary<string, string> tokens)
    {
        if (tokens.TryGetValue("BackgroundImage", out string? reference)
            && TryLoadImageBrush(reference, out IBrush? image))
        {
            application.Resources["AppBackground"] = image;
            return;
        }

        if (application.TryGetResource("Bg", null, out object? solid) && solid is IBrush brush)
            application.Resources["AppBackground"] = brush;
    }

    private static bool TryLoadImageBrush(string reference, out IBrush? brush)
    {
        brush = null;
        try
        {
            var uri = new Uri(reference, UriKind.Absolute);
            // Built-in/extension textures ship as avares:// resources; a user file path is opened directly.
            using Stream stream = uri.IsAbsoluteUri && uri.Scheme == "file"
                ? File.OpenRead(uri.LocalPath)
                : AssetLoader.Open(uri);
            brush = new ImageBrush(new Bitmap(stream)) { Stretch = Stretch.UniformToFill };
            return true;
        }
        catch (Exception ex) when (ex is UriFormatException or IOException or FileNotFoundException)
        {
            // A missing/unreadable texture falls back to the solid background rather than crashing the theme.
            System.Diagnostics.Trace.TraceWarning($"Theme background image '{reference}' could not be loaded ({ex.Message}).");
            return false;
        }
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
