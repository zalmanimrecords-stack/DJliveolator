using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Asset regenerator (doc 30) — NOT a behavioural test. Bakes the ANALOG theme's chrome+wood background
/// texture into the App's source assets: a brushed-chrome top band over warm wood planks with vertical
/// grain, all drawn procedurally (deterministic — no Random) so it regenerates byte-stably. Skipped in CI;
/// un-skip and run locally to regenerate src/Liveolator.App/Assets/Themes/analog/background.png.
/// </summary>
public sealed class AnalogBackgroundBaker
{
    private const int W = 1600;
    private const int H = 1000;
    private const int ChromeHeight = 128;
    private const int Planks = 6;

    [AvaloniaFact(Skip = "Manual asset regenerator (doc 30); produces src/Liveolator.App/Assets/Themes/analog/background.png")]
    public void Bake_chrome_and_wood_background()
    {
        string dir = Path.Combine(RepoRoot(), "src", "Liveolator.App", "Assets", "Themes", "analog");
        Directory.CreateDirectory(dir);

        using var bitmap = new RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96));
        using (DrawingContext ctx = bitmap.CreateDrawingContext())
        {
            DrawWood(ctx);
            DrawChromeBand(ctx);
        }
        bitmap.Save(Path.Combine(dir, "background.png"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(File.Exists(Path.Combine(dir, "background.png")));
    }

    private static void DrawWood(DrawingContext ctx)
    {
        var baseWood = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x3A, 0x26, 0x14), 0),
                new GradientStop(Color.FromRgb(0x2A, 0x1A, 0x0C), 0.55),
                new GradientStop(Color.FromRgb(0x1E, 0x13, 0x08), 1),
            },
        };
        ctx.DrawRectangle(baseWood, null, new Rect(0, 0, W, H));

        double plankWidth = (double)W / Planks;
        for (int p = 0; p < Planks; p++)
        {
            double x0 = p * plankWidth;
            // Vertical wood grain inside the plank: faint light/dark streaks, offset per plank so seams read.
            for (int i = 0; i < 26; i++)
            {
                double gx = x0 + (plankWidth * (i + 0.5) / 26.0);
                double wobble = 3.0 * Math.Sin((i * 1.7) + (p * 2.3));
                byte a = (byte)(10 + (int)(10 * (0.5 + 0.5 * Math.Sin(i * 0.9 + p))));
                bool light = (i + p) % 2 == 0;
                Color c = light ? Color.FromArgb(a, 0x7A, 0x55, 0x30) : Color.FromArgb(a, 0x10, 0x09, 0x03);
                ctx.DrawLine(new Pen(new SolidColorBrush(c), 1.4),
                    new Point(gx + wobble, 0), new Point(gx - wobble, H));
            }
            // Plank seam: a dark groove with a faint lit edge on its right.
            double seam = x0;
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x09, 0x03)), 2.2), new Point(seam, 0), new Point(seam, H));
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x8A, 0x63, 0x3A)), 1.0), new Point(seam + 1.6, 0), new Point(seam + 1.6, H));
        }
    }

    private static void DrawChromeBand(DrawingContext ctx)
    {
        var chrome = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xDA, 0xDE, 0xE3), 0),
                new GradientStop(Color.FromRgb(0x9A, 0xA0, 0xA8), 0.38),
                new GradientStop(Color.FromRgb(0xC2, 0xC7, 0xCD), 0.62),
                new GradientStop(Color.FromRgb(0x7C, 0x82, 0x8A), 0.85),
                new GradientStop(Color.FromRgb(0xA8, 0xAE, 0xB5), 1),
            },
        };
        var band = new Rect(0, 0, W, ChromeHeight);
        ctx.DrawRectangle(chrome, null, band);

        // Brushed striations: fine horizontal lines alternating faint light/dark.
        for (int y = 2; y < ChromeHeight; y += 2)
        {
            byte a = (byte)(8 + (int)(8 * (0.5 + 0.5 * Math.Sin(y * 0.7))));
            bool light = (y / 2) % 2 == 0;
            Color c = light ? Color.FromArgb(a, 0xFF, 0xFF, 0xFF) : Color.FromArgb(a, 0x30, 0x34, 0x39);
            ctx.DrawLine(new Pen(new SolidColorBrush(c), 1), new Point(0, y), new Point(W, y));
        }

        // Top highlight + bottom shadow lip where the chrome meets the wood.
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xF2, 0xF5, 0xF8)), 2), new Point(0, 2), new Point(W, 2));
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0x3A, 0x3E, 0x44)), 2), new Point(0, ChromeHeight - 2), new Point(W, ChromeHeight - 2));
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)), 5), new Point(0, ChromeHeight + 3), new Point(W, ChromeHeight + 3));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
