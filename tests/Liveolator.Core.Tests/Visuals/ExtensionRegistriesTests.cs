using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;

namespace Liveolator.Core.Tests.Visuals;

public sealed class ExtensionRegistriesTests
{
    [Fact]
    public void VisualRegistry_ReplacesAndRemovesOnePackage()
    {
        var registry = new VisualEffectRegistry();
        var effect = new VisualEffectDescriptor(
            "com.example.fx/echo", "1.0.0", "com.example.fx", "shaders/echo.glsl",
            new[] { new VisualEffectParameter("feedback", "uFeedback", 0, 1, 0.5) });

        registry.ReplacePackage("com.example.fx", new[] { effect });

        Assert.True(registry.TryGet(effect.EffectId, effect.Version, out VisualEffectDescriptor found));
        Assert.Equal("uFeedback", found.Parameters[0].Uniform);
        registry.RemovePackage("com.example.fx");
        Assert.Empty(registry.Effects);
    }

    [Fact]
    public void ThemeManager_AllowsTokensAndRejectsUnknownOrXamlLikeValues()
    {
        var manager = new UiThemeManager();
        var valid = new UiThemeDefinition(
            "com.example.theme/night",
            "Night",
            new Dictionary<string, string>
            {
                ["AccentColor"] = "#3366FF",
                ["PanelRadius"] = "16",
                ["UiFontFamily"] = "Inter",
            });
        var invalid = valid with
        {
            Tokens = new Dictionary<string, string> { ["ControlTemplate"] = "<ControlTemplate />" },
        };

        Assert.True(manager.Validate(valid).IsValid);
        Assert.False(manager.Validate(invalid).IsValid);
    }

    [Fact]
    public void EffectAndMacroTargets_RoundTripStableInstanceAddress()
    {
        var effect = new EffectRef(
            "com.example.fx/echo", "2.0.0", "echo-instance",
            new Dictionary<string, double> { ["feedback"] = 0.4 });
        var target = new MacroTarget(1, "echo-instance", "feedback");

        Assert.Equal("echo-instance", effect.InstanceId);
        Assert.Equal(effect.InstanceId, target.EffectInstanceId);
    }
}
