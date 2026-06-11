using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Liveolator.App.Controls;
using Liveolator.App.Theme;
using Liveolator.Core.Settings;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Whole-look proof of the ANALOG theme (doc 30): applies the built-in theme to the running app and renders
/// a panel of plain knobs/faders over the chrome+wood background — the controls pick up the vintage amber
/// arc + ivory cap/thumb from the theme tokens, and the window background is the texture image. Writes
/// artifacts/ui-shots/analog-theme.png. Resets the app background afterward so other shots stay clean.
/// </summary>
public sealed class AnalogThemeShot
{
    [AvaloniaFact]
    public void Render_analog_theme_to_png()
    {
        Application app = Application.Current!;
        UiThemeDefinition analog = BuiltInUiThemes.All.First(t => t.Id == BuiltInUiThemes.AnalogId);

        try
        {
            UiThemeApplier.Apply(app, analog);

            IBrush appBg = Brush(app, "AppBackground");
            IBrush panel = Brush(app, "S1");
            IBrush text = Brush(app, "Text");

            var knobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (double v in new[] { 0.2, 0.5, 0.8 })
                knobs.Children.Add(new Knob { Value = v, Width = 64, Height = 64 });
            knobs.Children.Add(new Fader { Value = 0.6, Width = 42, Height = 150 });

            var card = new Border
            {
                Background = panel,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(26),
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock { Text = "ANALOG", Foreground = text, FontSize = 22, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        knobs,
                    },
                },
            };

            var window = new Window
            {
                Background = appBg,
                Width = 560,
                Height = 340,
                Content = new Border { Padding = new Thickness(40), Child = card },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "analog-theme.png");
            window.CaptureRenderedFrame()?.Save(outPath);
            window.Close();

            Assert.True(File.Exists(outPath));
            Assert.IsType<ImageBrush>(appBg); // the chrome+wood texture actually loaded
        }
        finally
        {
            // Reset the (global) app background so later shots in this assembly aren't drawn on the texture.
            if (app.TryGetResource("Bg", null, out object? bg) && bg is IBrush solid)
                app.Resources["AppBackground"] = solid;
        }
    }

    private static IBrush Brush(Application app, string key)
    {
        Assert.True(app.TryGetResource(key, null, out object? value), $"resource '{key}' missing");
        return Assert.IsAssignableFrom<IBrush>(value);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
