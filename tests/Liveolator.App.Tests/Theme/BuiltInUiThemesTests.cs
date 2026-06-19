using Liveolator.App.Theme;
using Liveolator.Core.Settings;

namespace Liveolator.App.Tests.Theme;

public sealed class BuiltInUiThemesTests
{
    private static readonly string[] ResetTokens =
    [
        "WaveMidColor", "WaveHighColor",
        "KnobArcColor", "KnobTrackColor", "KnobCapColor", "KnobPointerColor",
        "FaderFillColor", "FaderTrackColor", "FaderThumbColor",
        "PanelRadius", "ControlRadius",
    ];

    [Fact]
    public void Register_adds_valid_brasswork_theme()
    {
        var themes = new UiThemeManager();

        BuiltInUiThemes.Register(themes);

        Assert.True(themes.TryGet(BuiltInUiThemes.BrassworkId, out UiThemeDefinition theme));
        Assert.Equal("#D78A16", theme.Tokens["AccentColor"]);
        Assert.Equal("#E89A18", theme.Tokens["WaveformColor"]);
        // Waveform 3-band colours are the VirtualDJ scheme, consistent across themes (owner-requested).
        Assert.Equal("#E23B2E", theme.Tokens["KickColor"]);
        Assert.Equal("#39C24A", theme.Tokens["WaveMidColor"]);
        Assert.Equal("#A036A6E8", theme.Tokens["WaveHighColor"]);
    }

    [Fact]
    public void Register_adds_valid_retro_scifi_theme_with_cut_hardware_shape()
    {
        var themes = new UiThemeManager();

        BuiltInUiThemes.Register(themes);

        Assert.True(themes.TryGet(BuiltInUiThemes.RetroSciFiId, out UiThemeDefinition theme));
        Assert.True(themes.Validate(theme).IsValid);
        Assert.Equal("#E2F05A", theme.Tokens["AccentColor"]);
        Assert.Equal("#07080A", theme.Tokens["BgColor"]);
        Assert.Equal("2", theme.Tokens["PanelRadius"]);
        Assert.Equal("0", theme.Tokens["ControlRadius"]);
        Assert.Equal("#101315", theme.Tokens["KnobCapColor"]);
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
    public void Built_in_themes_define_reset_tokens_for_clean_theme_switching()
    {
        foreach (UiThemeDefinition theme in BuiltInUiThemes.All)
        {
            foreach (string key in ResetTokens)
                Assert.True(theme.Tokens.ContainsKey(key), $"{theme.Id} must define {key}");
        }
    }
}
