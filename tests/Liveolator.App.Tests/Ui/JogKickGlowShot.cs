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
/// Visual check for the jog's phosphorescent green kick-glow (doc 19): three jogs over a beat grid — one
/// landed on a beat line (bright glow), one mid-beat (faint), one not pulsing (no glow). Writes
/// artifacts/ui-shots/jog-kick-glow.png so the rim flash can be compared by eye (UiShots can't show it —
/// no track plays headless).
/// </summary>
public sealed class JogKickGlowShot
{
    private static readonly double[] Grid = { 0.0, 0.25, 0.5, 0.75, 1.0 };
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6));
    private static readonly IBrush AppBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x13));

    [AvaloniaFact]
    public void Render_jog_kick_glow_to_png()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Center };
        // On the beat (bright), mid-beat (faint), and playing-off (no glow).
        row.Children.Add(NewJog(progress: 0.5, pulsing: true));   // exactly on a beat line → full glow
        row.Children.Add(NewJog(progress: 0.40, pulsing: true));  // mid-beat → faint glow
        row.Children.Add(NewJog(progress: 0.5, pulsing: false));  // not playing → no glow

        var window = new Window
        {
            Background = AppBg,
            Width = 660,
            Height = 260,
            Content = new Border { Padding = new Thickness(28), Child = row },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, "jog-kick-glow.png");
        window.CaptureRenderedFrame()?.Save(outPath);
        window.Close();

        Assert.True(File.Exists(outPath));
    }

    private static Jog NewJog(double progress, bool pulsing) => new()
    {
        Width = 176,
        Height = 176,
        IsEnabled = true,
        ArcBrush = Accent,
        Progress = progress,
        BeatGrid = Grid,
        IsBeatPulsing = pulsing,
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
