using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Liveolator.Core.Skins;

namespace Liveolator.App.Skins;

/// <summary>
/// Applies the active parametric control skins (doc 30) to the running <see cref="Application"/> by writing
/// the control-brush resources (<c>KnobArc</c>/<c>KnobTrack</c>/<c>KnobCap</c>/<c>KnobPointer</c> and
/// <c>FaderFill</c>/<c>FaderTrack</c>/<c>FaderThumb</c>) that the Knob/Fader styles bind to via
/// DynamicResource — so every control updates live, no restart. A colour the skin omits falls back to the
/// current theme token, so switching skins is reversible: passing <c>null</c> for a control resets it fully
/// to the themed look. Call after the UI theme is applied so the fallbacks read the themed colours.
/// </summary>
public static class ControlSkinApplier
{
    public static void Apply(Application application, ControlSkinFile? knob, ControlSkinFile? slider)
    {
        ArgumentNullException.ThrowIfNull(application);

        SetBrush(application, "KnobArc", knob?.Accent, "AccentColor");
        SetBrush(application, "KnobTrack", knob?.Track, "S4Color");
        SetBrush(application, "KnobCap", knob?.Body, "S2Color");
        SetBrush(application, "KnobPointer", knob?.Pointer, "TextColor");

        SetBrush(application, "FaderFill", slider?.Accent, "AccentColor");
        SetBrush(application, "FaderTrack", slider?.Track, "S4Color");
        // The slider thumb takes the body colour, falling back to the pointer colour when only that is set.
        SetBrush(application, "FaderThumb", slider?.Body ?? slider?.Pointer, "TextColor");
    }

    private static void SetBrush(Application application, string brushKey, string? hex, string fallbackColorKey)
    {
        Color color;
        if (!string.IsNullOrWhiteSpace(hex))
            color = Color.Parse(hex);
        else if (TryGetThemeColor(application, fallbackColorKey, out Color themed))
            color = themed;
        else
            return; // No skin colour and no resolvable theme fallback — leave the existing resource untouched.

        application.Resources[brushKey] = new SolidColorBrush(color);
    }

    private static bool TryGetThemeColor(Application application, string colorKey, out Color color)
    {
        if (application.TryGetResource(colorKey, null, out object? value) && value is Color c)
        {
            color = c;
            return true;
        }
        color = default;
        return false;
    }
}
