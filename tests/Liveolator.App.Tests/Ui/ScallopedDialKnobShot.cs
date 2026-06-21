using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Visual-parity render of the Retro Sci-Fi vintage cream-bakelite scalloped amp-dial knob across the
/// value range and at three sizes (EQ-small to large), in that theme's cream/sepia/brass palette.
/// Produces artifacts/ui-shots/scalloped-dial-knob.png so the amp-knob look can be checked by eye.
/// </summary>
public class ScallopedDialKnobShot
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#8A7350"));
    private static readonly IBrush Plate = new SolidColorBrush(Color.Parse("#D7C7A2"));
    private static readonly IBrush Cap = new SolidColorBrush(Color.Parse("#ECE2C8"));
    private static readonly IBrush Brass = new SolidColorBrush(Color.Parse("#B2935E"));
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.Parse("#11181A"));
    private static readonly IBrush AppBg = new SolidColorBrush(Color.Parse("#07080A"));

    [AvaloniaFact]
    public void Render_scalloped_dial_knobs_to_png()
    {
        var rows = new StackPanel { Spacing = 28 };
        foreach (double dim in new[] { 56.0, 80.0, 120.0 })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (double v in new[] { 0.0, 0.3, 0.5, 0.75, 1.0 })
                row.Children.Add(Build(v, dim, true));
            row.Children.Add(Build(0.5, dim, false));
            rows.Children.Add(row);
        }

        var panel = new Border
        {
            Background = PanelBg,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(34),
            Child = rows,
        };

        var window = new Window
        {
            Background = AppBg,
            Width = 760,
            Height = 540,
            Content = new Border { Padding = new Thickness(24), Child = panel },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "scalloped-dial-knob.png"));

        Assert.True(File.Exists(Path.Combine(outDir, "scalloped-dial-knob.png")));
    }

    private static Knob Build(double value, double dim, bool enabled) => new()
    {
        Variant = KnobStyle.ScallopedDial,
        Value = value,
        ArcBrush = Ink, TrackBrush = Plate, CapBrush = Cap, PointerBrush = Brass,
        Width = dim, Height = dim, IsEnabled = enabled,
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
