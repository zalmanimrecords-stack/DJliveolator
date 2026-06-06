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
/// Isolated render of the skeuomorphic <see cref="Knob"/> and <see cref="Fader"/> controls straight onto
/// a styled panel — deliberately bypasses ServiceConfig (which opens native MIDI and cannot boot on a
/// machine with no MIDI devices). This is the visual-parity loop for the controls themselves: it produces
/// artifacts/ui-shots/hardware-controls.png so the hardware look can be compared by eye.
/// </summary>
public class HardwareControlsShot
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6));
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x26));
    private static readonly IBrush AppBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x13));

    [AvaloniaFact]
    public void Render_knobs_and_faders_to_png()
    {
        var knobRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (double v in new[] { 0.0, 0.25, 0.5, 0.78, 1.0 })
            knobRow.Children.Add(new Knob { Value = v, ArcBrush = Accent, Width = 64, Height = 64 });
        knobRow.Children.Add(new Knob { Value = 0.5, ArcBrush = Accent, Width = 64, Height = 64, IsEnabled = false });

        var faderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 34, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (double v in new[] { 0.2, 0.6, 0.9 })
            faderRow.Children.Add(new Fader { Value = v, FillBrush = Accent, Width = 46, Height = 230 });
        faderRow.Children.Add(new Fader { Value = 0.5, FillBrush = Accent, Width = 46, Height = 230, IsEnabled = false });

        var crossfader = new Fader { Orientation = Orientation.Horizontal, Value = 0.5, FillBrush = Accent, Width = 360, Height = 34 };

        var panel = new Border
        {
            Background = PanelBg,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(36),
            Child = new StackPanel
            {
                Spacing = 30,
                Children = { knobRow, faderRow, crossfader },
            },
        };

        var window = new Window
        {
            Background = AppBg,
            Width = 620,
            Height = 540,
            Content = new Border { Padding = new Thickness(24), Child = panel },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "hardware-controls.png"));

        Assert.True(File.Exists(Path.Combine(outDir, "hardware-controls.png")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
