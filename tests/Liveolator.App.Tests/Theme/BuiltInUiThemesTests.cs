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
        // Waveform 3-band colours are the VirtualDJ scheme, consistent across themes (owner-requested).
        Assert.Equal("#E23B2E", theme.Tokens["KickColor"]);
        Assert.Equal("#39C24A", theme.Tokens["WaveMidColor"]);
        Assert.Equal("#A036A6E8", theme.Tokens["WaveHighColor"]);
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
}
