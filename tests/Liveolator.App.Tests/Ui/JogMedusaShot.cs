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
/// Visual check for the jog's centre medusa: three jogs over the same analyzed low-band (kick) data — one
/// with the playhead on a kick (bell washed red by the bass), one between kicks (cool aqua), one not
/// playing (no wash). Writes artifacts/ui-shots/jog-medusa.png so the bass-reactive tint can be compared by
/// eye (UiShots can't show it — no track plays headless).
/// </summary>
public sealed class JogMedusaShot
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6));
    private static readonly IBrush Red = new SolidColorBrush(Color.FromRgb(0xE5, 0x54, 0x4A));
    private static readonly IBrush AppBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x13));

    // 64 buckets: a quiet low-end floor with a kick transient every 16 buckets (a 4-on-the-floor pattern).
    private static readonly float[] Kicks = BuildKicks();

    [AvaloniaFact]
    public void Render_jog_medusa_to_png()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(NewJog(progress: 32.0 / 63.0, active: true));  // on a kick → red bass wash
        row.Children.Add(NewJog(progress: 26.0 / 63.0, active: true));  // between kicks → cool aqua
        row.Children.Add(NewJog(progress: 32.0 / 63.0, active: false)); // not playing → no wash

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
        string outPath = Path.Combine(outDir, "jog-medusa.png");
        window.CaptureRenderedFrame()?.Save(outPath);
        window.Close();

        Assert.True(File.Exists(outPath));
    }

    private static float[] BuildKicks()
    {
        var kicks = new float[64];
        for (int i = 0; i < kicks.Length; i++)
            kicks[i] = i % 16 == 0 ? 1.0f : 0.06f;
        return kicks;
    }

    private static Jog NewJog(double progress, bool active) => new()
    {
        Width = 176,
        Height = 176,
        IsEnabled = true,
        ArcBrush = Accent,
        BassTintBrush = Red,
        Progress = progress,
        KickPeaks = Kicks,
        IsKickActive = active,
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
