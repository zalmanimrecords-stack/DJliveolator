using System.Collections.Generic;
using Liveolator.Core.Settings;

namespace Liveolator.Core.Tests.Settings;

/// <summary>
/// The theme token allow-list (doc 30): besides the surface/accent colours, a theme may carry per-control
/// colours (vintage knob/fader look) and a background image reference. Unknown tokens are still rejected.
/// </summary>
public sealed class UiThemeManagerTokenTests
{
    private static UiThemeDefinition Theme(IReadOnlyDictionary<string, string> tokens)
        => new("analog", "Analog", tokens);

    [Fact]
    public void Accepts_per_control_colour_tokens()
    {
        var result = new UiThemeManager().Validate(Theme(new Dictionary<string, string>
        {
            ["KnobCapColor"] = "#E8DCC2",
            ["KnobArcColor"] = "#E0922A",
            ["FaderThumbColor"] = "#E8DCC2",
        }));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Accepts_a_background_image_reference()
    {
        var result = new UiThemeManager().Validate(Theme(new Dictionary<string, string>
        {
            ["BackgroundImage"] = "avares://Liveolator.App/Assets/Skins/aurora/knob.png",
        }));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Rejects_a_malformed_control_colour()
    {
        var result = new UiThemeManager().Validate(Theme(new Dictionary<string, string>
        {
            ["KnobCapColor"] = "cream",
        }));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_an_empty_background_image()
    {
        var result = new UiThemeManager().Validate(Theme(new Dictionary<string, string>
        {
            ["BackgroundImage"] = "  ",
        }));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Accepts_a_known_knob_style_enum_token()
    {
        var result = new UiThemeManager().Validate(Theme(new Dictionary<string, string>
        {
            ["KnobStyle"] = "ScallopedDial",
        }));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Rejects_an_unknown_knob_style_value()
    {
        var result = new UiThemeManager().Validate(Theme(new Dictionary<string, string>
        {
            ["KnobStyle"] = "Triangle",
        }));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Still_rejects_an_unknown_token()
    {
        var result = new UiThemeManager().Validate(Theme(new Dictionary<string, string>
        {
            ["BananaColor"] = "#FFEE00",
        }));

        Assert.False(result.IsValid);
    }
}
