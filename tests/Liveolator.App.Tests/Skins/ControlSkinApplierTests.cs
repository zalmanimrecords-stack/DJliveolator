using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Liveolator.App.Skins;
using Liveolator.Core.Skins;

namespace Liveolator.App.Tests.Skins;

/// <summary>
/// The control-skin applier (doc 30) writes the control-brush resources the Knob/Fader styles bind to, so a
/// skin takes effect live. Runs in the headless app so the real App.axaml tokens/brush keys are present.
/// Assertions compare against the theme Color tokens (not prior state), since AvaloniaFacts share one
/// Application.Current.
/// </summary>
public sealed class ControlSkinApplierTests
{
    private static Color BrushColor(Application app, string brushKey)
    {
        Assert.True(app.TryGetResource(brushKey, null, out object? value), $"resource '{brushKey}' missing");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static Color ThemeColor(Application app, string colorKey)
    {
        Assert.True(app.TryGetResource(colorKey, null, out object? value), $"token '{colorKey}' missing");
        return Assert.IsType<Color>(value);
    }

    [AvaloniaFact]
    public void Apply_overrides_declared_colours_and_falls_back_to_theme_for_omitted()
    {
        Application app = Application.Current!;

        var knob = new ControlSkinFile { Name = "Red", Kind = ControlSkinKind.Knob, Accent = "#FF0000" };
        ControlSkinApplier.Apply(app, knob, slider: null);

        Assert.Equal(Color.Parse("#FF0000"), BrushColor(app, "KnobArc"));   // declared → overridden
        Assert.Equal(ThemeColor(app, "S4Color"), BrushColor(app, "KnobTrack")); // omitted → themed S4 fallback
    }

    [AvaloniaFact]
    public void Apply_null_resets_a_previously_skinned_control_to_the_theme()
    {
        Application app = Application.Current!;

        ControlSkinApplier.Apply(app, new ControlSkinFile { Name = "Green", Kind = ControlSkinKind.Knob, Accent = "#00FF00" }, null);
        Assert.Equal(Color.Parse("#00FF00"), BrushColor(app, "KnobArc"));

        ControlSkinApplier.Apply(app, knob: null, slider: null);
        Assert.Equal(ThemeColor(app, "AccentColor"), BrushColor(app, "KnobArc")); // back to the themed accent
    }

    // Regression: SaveAsync runs on a ReactiveUI background thread, so the applier seam is invoked off the
    // UI thread. Mutable brushes (SolidColorBrush) are AvaloniaObjects whose ctor enforces UI-thread access,
    // which previously crashed the app ("Call from invalid thread"). The seam must marshal to the UI thread.
    [AvaloniaFact]
    public async Task ApplicationApplier_invoked_off_the_ui_thread_does_not_throw_and_skins_the_control()
    {
        Application app = Application.Current!;
        var applier = new ApplicationControlSkinApplier();
        var knob = new ControlSkinFile { Name = "Crimson", Kind = ControlSkinKind.Knob, Accent = "#AB12CD" };

        Exception? captured = await Task.Run(() =>
        {
            try { applier.Apply(knob, slider: null); return (Exception?)null; }
            catch (Exception ex) { return ex; }
        });

        Assert.Null(captured);
        Assert.Equal(Color.Parse("#AB12CD"), BrushColor(app, "KnobArc"));
    }
}
