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

        UiThemeApplier.Apply(app, Theme(("BackgroundImage", "avares://Liveolator.App/Assets/Themes/analog/background.png")));

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

    [AvaloniaFact]
    public void Theme_without_background_image_resets_AppBackground_to_solid()
    {
        Application app = Application.Current!;

        UiThemeApplier.Apply(app, Theme(("BackgroundImage", "avares://Liveolator.App/Assets/Themes/analog/background.png")));
        Assert.IsType<ImageBrush>(Resource(app, "AppBackground"));

        UiThemeApplier.Apply(app, Theme()); // no image -> back to solid
        Assert.IsAssignableFrom<ISolidColorBrush>(Resource(app, "AppBackground"));
    }

    private static object Resource(Application app, string key)
    {
        Assert.True(app.TryGetResource(key, null, out object? value));
        return value!;
    }
}
