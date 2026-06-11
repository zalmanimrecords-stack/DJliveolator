using Liveolator.App.Theme;
using Liveolator.Core.Settings;

namespace Liveolator.App.Tests.Theme;

public sealed class BuiltInUiThemesTests
{
    [Fact]
    public void Register_adds_valid_brasswork_theme()
    {
        var themes = new UiThemeManager();

        BuiltInUiThemes.Register(themes);

        Assert.True(themes.TryGet(BuiltInUiThemes.BrassworkId, out UiThemeDefinition theme));
        Assert.Equal("#D78A16", theme.Tokens["AccentColor"]);
        Assert.Equal("#E89A18", theme.Tokens["WaveformColor"]);
        Assert.Equal("#7DBA50", theme.Tokens["KickColor"]);
    }

    [Fact]
    public void Register_adds_valid_analog_theme_with_background_and_vintage_knob()
    {
        var themes = new UiThemeManager();

        BuiltInUiThemes.Register(themes);

        Assert.True(themes.TryGet(BuiltInUiThemes.AnalogId, out UiThemeDefinition theme));
        // The whole definition must pass validation (every token is on the allow-list).
        Assert.True(themes.Validate(theme).IsValid);
        Assert.Equal("#E8DCC2", theme.Tokens["KnobCapColor"]);
        Assert.Equal("#E0922A", theme.Tokens["AccentColor"]);
        Assert.StartsWith("avares://", theme.Tokens["BackgroundImage"]);
    }
}
