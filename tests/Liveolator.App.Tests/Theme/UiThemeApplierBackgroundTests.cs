using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Liveolator.App.Theme;
using Liveolator.Core.Settings;

namespace Liveolator.App.Tests.Theme;

/// <summary>
/// Applying a theme with the new tokens (doc 30): a <c>BackgroundImage</c> replaces the solid
/// <c>AppBackground</c> with an <see cref="ImageBrush"/>; a per-control colour token retints the matching
/// control brush; and a theme without a background image resets <c>AppBackground</c> to the solid colour.
/// Runs in the headless app so the real App.axaml resources are present.
/// </summary>
public sealed class UiThemeApplierBackgroundTests
{
    private static UiThemeDefinition Theme(params (string Key, string Value)[] tokens)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["BgColor"] = "#101418" };
        foreach ((string key, string value) in tokens)
            map[key] = value;
        return new UiThemeDefinition("t", "T", map);
    }

    [AvaloniaFact]
    public void BackgroundImage_token_sets_AppBackground_to_an_image_brush()
    {
        Application app = Application.Current!;

        UiThemeApplier.Apply(app, Theme(("BackgroundImage", "avares://Liveolator.App/Assets/Skins/aurora/knob.png")));

        Assert.True(app.TryGetResource("AppBackground", null, out object? value));
        Assert.IsType<ImageBrush>(value);
    }

    [AvaloniaFact]
    public void Per_control_colour_token_retints_the_control_brush()
    {
        Application app = Application.Current!;

        UiThemeApplier.Apply(app, Theme(("KnobCapColor", "#E8DCC2")));

        Assert.True(app.TryGetResource("KnobCap", null, out object? value));
        Assert.Equal(Color.Parse("#E8DCC2"), Assert.IsAssignableFrom<ISolidColorBrush>(value).Color);
    }

    // Radius tokens must land as CornerRadius, not a bare Double: the control styles bind them to a
    // CornerRadius property, and on style re-evaluation Avalonia casts the resource directly (no implicit
    // Double->CornerRadius conversion), so a Double resource crashes the app on the next layout pass.
    [AvaloniaFact]
    public void Radius_token_is_applied_as_a_corner_radius()
    {
        Application app = Application.Current!;

        UiThemeApplier.Apply(app, Theme(("PanelRadius", "2"), ("ControlRadius", "0")));

        Assert.Equal(new CornerRadius(2), Assert.IsType<CornerRadius>(Resource(app, "PanelRadius")));
        Assert.Equal(new CornerRadius(0), Assert.IsType<CornerRadius>(Resource(app, "ControlRadius")));
    }

    [AvaloniaFact]
    public void Theme_without_background_image_resets_AppBackground_to_solid()
    {
        Application app = Application.Current!;

        UiThemeApplier.Apply(app, Theme(("BackgroundImage", "avares://Liveolator.App/Assets/Skins/aurora/knob.png")));
        Assert.IsType<ImageBrush>(Resource(app, "AppBackground"));

        UiThemeApplier.Apply(app, Theme()); // no image -> back to solid
        Assert.IsAssignableFrom<ISolidColorBrush>(Resource(app, "AppBackground"));
    }

    [AvaloniaFact]
    public void Knob_style_token_sets_the_knob_style_resource()
    {
        Application app = Application.Current!;

        UiThemeApplier.Apply(app, Theme(("KnobStyle", "ScallopedDial")));

        Assert.Equal(Liveolator.App.Controls.KnobStyle.ScallopedDial, Resource(app, "KnobStyle"));
    }

    [AvaloniaFact]
    public void Theme_without_knob_style_resets_to_rotary()
    {
        Application app = Application.Current!;

        UiThemeApplier.Apply(app, Theme(("KnobStyle", "ScallopedDial")));
        Assert.Equal(Liveolator.App.Controls.KnobStyle.ScallopedDial, Resource(app, "KnobStyle"));

        UiThemeApplier.Apply(app, Theme()); // no token -> back to the default rotary knob
        Assert.Equal(Liveolator.App.Controls.KnobStyle.Rotary, Resource(app, "KnobStyle"));
    }

    private static object Resource(Application app, string key)
    {
        Assert.True(app.TryGetResource(key, null, out object? value));
        return value!;
    }
}
