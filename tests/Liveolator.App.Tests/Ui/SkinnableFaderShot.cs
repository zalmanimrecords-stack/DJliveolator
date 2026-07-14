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
/// Visual-parity loop for the PNG-skinned fader (doc 30): loads the "aurora" track+thumb from the App's
/// avares:// resources, renders <see cref="SkinnableFader"/> at several values plus one with no skin
/// (vector fallback), and writes artifacts/ui-shots/skinnable-fader.png. Proves the track+thumb pipeline
/// end to end — value → thumb position → pixels — through the shipping resource path.
/// </summary>
public sealed class SkinnableFaderShot
{
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x26));
    private static readonly IBrush AppBg = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x13));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6));

    [AvaloniaFact]
    public void Render_skinned_fader_row_to_png()
    {
        FaderSkin skin = LoadAuroraFaderSkin();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 34, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (double v in new[] { 0.15, 0.5, 0.85 })
            row.Children.Add(new SkinnableFader { Skin = skin, Value = v, Width = 46, Height = 220 });
        // Last fader has no skin -> inherited vector fallback, proving back-compat in the same shot.
        row.Children.Add(new SkinnableFader { Value = 0.5, FillBrush = Accent, Width = 46, Height = 220 });

        var window = new Window
        {
            Background = AppBg,
            Width = 360,
            Height = 300,
            Content = new Border
            {
                Padding = new Thickness(24),
                Child = new Border { Background = PanelBg, CornerRadius = new CornerRadius(16), Padding = new Thickness(24), Child = row },
            },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string outDir = Path.Combine(RepoRoot(), "artifacts", "ui-shots");
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, "skinnable-fader.png");
        window.CaptureRenderedFrame()?.Save(outPath);
        window.Close();

        Assert.True(File.Exists(outPath));
    }

    private static FaderSkin LoadAuroraFaderSkin()
    {
        using Stream trackStream = AssetLoader.Open(new Uri("avares://Liveolator.App/Assets/Skins/aurora/fader-track.png"));
        using Stream thumbStream = AssetLoader.Open(new Uri("avares://Liveolator.App/Assets/Skins/aurora/fader-thumb.png"));
        return new FaderSkin(new Bitmap(trackStream), new Bitmap(thumbStream));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
