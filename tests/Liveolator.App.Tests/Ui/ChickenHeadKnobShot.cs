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
/// Visual-parity render of the Retro Sci-Fi chicken-head amp knob across the value range, in that theme's
/// palette. Produces artifacts/ui-shots/chicken-head-knob.png so the amp-knob look can be checked by eye.
/// </summary>
public class ChickenHeadKnobShot
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#E2F05A"));
    private static readonly IBrush Cap = new SolidColorBrush(Color.Parse("#101315"));
    private static readonly IBrush Pointer = new SolidColorBrush(Color.Parse("#FAFF9A"));
    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#243236"));
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.Parse("#11181A"));
    private static readonly IBrush AppBg = new SolidColorBrush(Color.Parse("#07080A"));

    [AvaloniaFact]
    public void Render_chicken_head_knobs_to_png()
    {
        var knobRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (double v in new[] { 0.0, 0.25, 0.5, 0.78, 1.0 })
            knobRow.Children.Add(new Knob
            {
                Variant = KnobStyle.ChickenHead,
                Value = v, ArcBrush = Accent, CapBrush = Cap, PointerBrush = Pointer, TrackBrush = Track,
                Width = 72, Height = 72,
            });
        knobRow.Children.Add(new Knob
        {
            Variant = KnobStyle.ChickenHead,
            Value = 0.5, ArcBrush = Accent, CapBrush = Cap, PointerBrush = Pointer, TrackBrush = Track,
            Width = 72, Height = 72, IsEnabled = false,
        });

        var panel = new Border
        {
            Background = PanelBg,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(36),
            Child = knobRow,
        };

        var window = new Window
        {
            Background = AppBg,
            Width = 720,
            Height = 200,
            Content = new Border { Padding = new Thickness(24), Child = panel },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "chicken-head-knob.png"));

        Assert.True(File.Exists(Path.Combine(outDir, "chicken-head-knob.png")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
