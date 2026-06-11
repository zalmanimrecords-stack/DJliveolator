using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Liveolator.App.Controls;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Asset regenerator (doc 30) — NOT a behavioural test. Bakes the current vector <see cref="Knob"/> into
/// a vertical filmstrip PNG (one frame per rotation step) and writes it to the App's source assets, so the
/// first shipping skin ("aurora") is pixel-faithful to today's look and the filmstrip pipeline is proven.
/// Swapping in a photographed/3D-rendered strip is then the only step to full realism. Skipped in CI so it
/// never rewrites a source-controlled asset; un-skip and run locally to regenerate.
/// </summary>
public sealed class KnobFilmstripBaker
{
    private const int FrameCount = 65;
    private const int FrameSize = 128;
    // The knob is inset in the frame so its cast shadow fades inside a transparent margin instead of
    // being clipped to a hard square at the frame edge (which read as a dark box on the panel).
    private const int KnobSize = 104;
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6));

    [AvaloniaFact(Skip = "Manual asset regenerator (doc 30); produces src/Liveolator.App/Assets/Skins/aurora/knob.png")]
    public void Bake_vector_knob_into_filmstrip()
    {
        var strip = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        for (int i = 0; i < FrameCount; i++)
        {
            double value = (double)i / (FrameCount - 1);
            strip.Children.Add(new Panel
            {
                Width = FrameSize,
                Height = FrameSize,
                Children =
                {
                    new Knob
                    {
                        Value = value,
                        ArcBrush = Accent,
                        Width = KnobSize,
                        Height = KnobSize,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            });
        }

        // Render into a RenderTargetBitmap (NOT a captured Window): it clears to fully transparent, so the
        // frames carry an alpha channel and composite onto any panel colour — a captured headless window is
        // opaque black and would bake a dark square behind every frame.
        var size = new Size(FrameSize, FrameSize * FrameCount);
        strip.Measure(size);
        strip.Arrange(new Rect(size));
        Dispatcher.UIThread.RunJobs();

        string outPath = Path.Combine(
            RepoRoot(), "src", "Liveolator.App", "Assets", "Skins", "aurora", "knob.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        using (var bitmap = new RenderTargetBitmap(new PixelSize(FrameSize, FrameSize * FrameCount), new Vector(96, 96)))
        {
            bitmap.Render(strip);
            bitmap.Save(outPath);
        }

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
