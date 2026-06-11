using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Liveolator.App.Tests.Ui;

/// <summary>
/// Asset regenerator (doc 30) — NOT a behavioural test. Bakes the two PNGs of the "aurora" fader skin (a
/// vertical track + a metallic thumb cap) into the App's source assets, in the same colour language as the
/// vector <c>Fader</c>, so the first shipping slider skin is coherent and the track+thumb pipeline is proven.
/// Swapping in photographed/3D PNGs is then the only step to full realism. Skipped in CI so it never rewrites
/// a source-controlled asset; un-skip and run locally to regenerate.
/// </summary>
public sealed class FaderSkinBaker
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xF6));

    [AvaloniaFact(Skip = "Manual asset regenerator (doc 30); produces src/Liveolator.App/Assets/Skins/aurora/fader-*.png")]
    public void Bake_fader_track_and_thumb()
    {
        string dir = Path.Combine(RepoRoot(), "src", "Liveolator.App", "Assets", "Skins", "aurora");
        Directory.CreateDirectory(dir);

        BakeTrack(Path.Combine(dir, "fader-track.png"));
        BakeThumb(Path.Combine(dir, "fader-thumb.png"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(File.Exists(Path.Combine(dir, "fader-track.png")));
        Assert.True(File.Exists(Path.Combine(dir, "fader-thumb.png")));
    }

    private static void BakeTrack(string path)
    {
        const int w = 14, h = 240;
        using var bitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (DrawingContext ctx = bitmap.CreateDrawingContext())
        {
            double cx = w / 2.0;
            var top = new Point(cx, 8);
            var bottom = new Point(cx, h - 8);
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xA8, 0, 0, 0)), 12) { LineCap = PenLineCap.Round }, top, bottom);
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x72, 0x70, 0x7E, 0x90)), 8) { LineCap = PenLineCap.Round }, top + new Point(-1.1, 0), bottom + new Point(-1.1, 0));
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x0D)), 7) { LineCap = PenLineCap.Round }, top, bottom);
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x26, 0x30, 0x3F)), 4) { LineCap = PenLineCap.Round }, top, bottom);
        }
        bitmap.Save(path);
    }

    private static void BakeThumb(string path)
    {
        const int w = 34, h = 18;
        using var bitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (DrawingContext ctx = bitmap.CreateDrawingContext())
        {
            var cap = new Rect(1, 1, w - 2, h - 2);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)), null, cap, 4, 4);
            ctx.DrawRectangle(CapGradient(), new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x3F, 0x52)), 1.2), cap, 4, 4);
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x74, 0xE4, 0xEB, 0xF3)), 1),
                new Point(cap.Left + 4, cap.Top + 2.5), new Point(cap.Right - 4, cap.Top + 2.5));
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x9A, 0x03, 0x06, 0x0B)), 1),
                new Point(cap.Left + 4, cap.Bottom - 2), new Point(cap.Right - 4, cap.Bottom - 2));

            double y = cap.Center.Y;
            DrawGroove(ctx, new Point(cap.Left + 4, y - 3), new Point(cap.Right - 4, y - 3));
            DrawGroove(ctx, new Point(cap.Left + 4, y + 3), new Point(cap.Right - 4, y + 3));
            ctx.DrawLine(new Pen(Accent, 2) { LineCap = PenLineCap.Round }, new Point(cap.Left + 4, y), new Point(cap.Right - 4, y));
        }
        bitmap.Save(path);
    }

    private static void DrawGroove(DrawingContext ctx, Point start, Point end)
    {
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x02, 0x04, 0x08)), 1.4) { LineCap = PenLineCap.Round }, start, end);
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x54, 0xA1, 0xAD, 0xBC)), 0.8) { LineCap = PenLineCap.Round },
            start + new Point(-0.5, -0.5), end + new Point(-0.5, -0.5));
    }

    private static IBrush CapGradient()
        => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xF0, 0x55, 0x62, 0x73), 0),
                new GradientStop(Color.FromArgb(0xF2, 0x34, 0x3E, 0x4D), 0.2),
                new GradientStop(Color.FromArgb(0xF6, 0x1B, 0x23, 0x2E), 0.58),
                new GradientStop(Color.FromArgb(0xFA, 0x0B, 0x10, 0x17), 0.82),
                new GradientStop(Color.FromArgb(0xF0, 0x21, 0x2A, 0x36), 1),
            },
        };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Liveolator.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
