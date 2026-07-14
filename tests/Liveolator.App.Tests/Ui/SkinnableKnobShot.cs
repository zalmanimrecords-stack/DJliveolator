using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Visual-parity loop for the PNG-skinned knob (doc 30): loads the "aurora" filmstrip from the App's
/// avares:// resources, renders a row of <see cref="SkinnableKnob"/> at several values plus one with no
/// skin (vector fallback), and writes artifacts/ui-shots/skinnable-knob.png. Proves the filmstrip pipeline
/// end to end — value → frame → pixels — through the same resource path the shipping app uses.
/// </summary>
public sealed class SkinnableKnobShot
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6));
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x26));
    private static readonly IBrush AppBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x13));

    [AvaloniaFact]
    public void Render_skinned_knob_row_to_png()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Liveolator.App/Assets/Skins/aurora/knob.png"));
        var skin = new KnobSkin(new Bitmap(stream), frameCount: 65);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (double v in new[] { 0.0, 0.25, 0.5, 0.78, 1.0 })
            row.Children.Add(new SkinnableKnob { Skin = skin, Value = v, Width = 64, Height = 64 });
        // Last knob has no skin -> inherited vector fallback, proving back-compat in the same shot.
        row.Children.Add(new SkinnableKnob { Value = 0.5, ArcBrush = Accent, Width = 64, Height = 64 });

        var panel = new Border
        {
            Background = PanelBg,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(36),
            Child = row,
        };
        var window = new Window
        {
            Background = AppBg,
            Width = 660,
            Height = 200,
            Content = new Border { Padding = new Thickness(24), Child = panel },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, "skinnable-knob.png");
        window.CaptureRenderedFrame()?.Save(outPath);
        window.Close();

        Assert.True(File.Exists(outPath));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
