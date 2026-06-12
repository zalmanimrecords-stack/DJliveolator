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
    public void Spartan_theme_is_valid_and_complete_so_apply_resets_cleanly()
    {
        var themes = new UiThemeManager();

        BuiltInUiThemes.Register(themes);

        Assert.True(themes.TryGet(BuiltInUiThemes.SpartanId, out UiThemeDefinition spartan));
        Assert.True(themes.Validate(spartan).IsValid);
        // Must define every per-control colour so "Apply" back to Spartan fully resets the knobs/faders
        // (no leftover tint from a previously-applied theme/skin), and carry NO background image (resets to solid).
        foreach (string key in new[]
        {
            "KnobArcColor", "KnobTrackColor", "KnobCapColor", "KnobPointerColor",
            "FaderFillColor", "FaderTrackColor", "FaderThumbColor",
        })
            Assert.True(spartan.Tokens.ContainsKey(key), $"Spartan must define {key}");

        Assert.Equal("#2F80F6", spartan.Tokens["AccentColor"]);
        Assert.Equal("#0C1017", spartan.Tokens["KnobCapColor"]);
        Assert.False(spartan.Tokens.ContainsKey("BackgroundImage"));
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
