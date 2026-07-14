using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Liveolator.App.Controls;
using Liveolator.App.Skins;
using Liveolator.Core.Skins;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Proves the Phase 2 app-wide path (doc 30): applying a control skin to the running application re-skins
/// PLAIN Knob/Fader instances (no per-instance brushes) through the Spartan styles + DynamicResource keys —
/// the same mechanism the Settings picker and startup use. Writes artifacts/ui-shots/control-skins-applied.png.
/// </summary>
public sealed class ControlSkinAppliedShot
{
    private static readonly IBrush AppBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x13));

    [AvaloniaFact]
    public void Applied_skin_reskins_plain_controls()
    {
        Application app = Application.Current!;
        try
        {
            ControlSkinApplier.Apply(app,
                new ControlSkinFile { Name = "Ember Knob", Kind = ControlSkinKind.Knob, Accent = "#E8821A", Body = "#1A1206", Pointer = "#FFE6C7" },
                new ControlSkinFile { Name = "Ember Slider", Kind = ControlSkinKind.Slider, Accent = "#E8821A" });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Center };
            // PLAIN controls — no ArcBrush/FillBrush set; they must inherit the applied skin via styles.
            foreach (double v in new[] { 0.3, 0.6, 0.9 })
                row.Children.Add(new Knob { Value = v, Width = 64, Height = 64 });
            row.Children.Add(new Fader { Value = 0.62, Width = 46, Height = 190 });

            var window = new Window
            {
                Background = AppBg,
                Width = 420,
                Height = 260,
                Content = new Border { Padding = new Thickness(28), Child = row },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "control-skins-applied.png");
            window.CaptureRenderedFrame()?.Save(outPath);
            window.Close();

            Assert.True(File.Exists(outPath));
        }
        finally
        {
            // Reset so other tests sharing Application.Current see the themed defaults again.
            ControlSkinApplier.Apply(app, knob: null, slider: null);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
