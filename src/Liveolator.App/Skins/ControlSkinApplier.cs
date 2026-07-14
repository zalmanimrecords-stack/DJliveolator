using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
    /// <param name="onWarning">
    /// Optional sink for non-fatal problems (a malformed skin colour). A <see cref="ControlSkinFile"/> is an
    /// unvalidated record and the MCP authoring session can apply one directly, so a bad <c>#RRGGBB</c> value
    /// can reach here; it is reported and degraded to the themed fallback rather than thrown — otherwise the
    /// exception escaped into the ReactiveUI default handler and was logged as a startup crash (doc 30).
    /// </param>
    public static void Apply(Application application, ControlSkinFile? knob, ControlSkinFile? slider, Action<string>? onWarning = null)
    {
        ArgumentNullException.ThrowIfNull(application);

        SetBrush(application, "KnobArc", knob?.Accent, "AccentColor", onWarning);
        SetBrush(application, "KnobTrack", knob?.Track, "S4Color", onWarning);
        SetBrush(application, "KnobCap", knob?.Body, "S2Color", onWarning);
        SetBrush(application, "KnobPointer", knob?.Pointer, "TextColor", onWarning);

        SetBrush(application, "FaderFill", slider?.Accent, "AccentColor", onWarning);
        SetBrush(application, "FaderTrack", slider?.Track, "S4Color", onWarning);
        // The slider thumb takes the body colour, falling back to the pointer colour when only that is set.
        SetBrush(application, "FaderThumb", slider?.Body ?? slider?.Pointer, "TextColor", onWarning);
    }

    private static void SetBrush(Application application, string brushKey, string? hex, string fallbackColorKey, Action<string>? onWarning)
    {
        Color color;
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out Color parsed))
        {
            color = parsed;
        }
        else
        {
            // A non-empty hex that fails to parse is a malformed skin colour — report it and degrade to the
            // theme fallback (treating it like an omitted colour) instead of throwing into the global handler.
            if (!string.IsNullOrWhiteSpace(hex))
                onWarning?.Invoke(
                    $"Control skin colour '{hex}' for '{brushKey}' is not a valid #RRGGBB/#AARRGGBB value; using the theme colour.");

            if (!TryGetThemeColor(application, fallbackColorKey, out color))
                return; // No skin colour and no resolvable theme fallback — leave the existing resource untouched.
        }

        // ImmutableSolidColorBrush is not an AvaloniaObject, so it is safe to construct off the UI thread
        // (the project's established pattern for cross-thread brushes); Apply still marshals the writes.
        application.Resources[brushKey] = new ImmutableSolidColorBrush(color);
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
