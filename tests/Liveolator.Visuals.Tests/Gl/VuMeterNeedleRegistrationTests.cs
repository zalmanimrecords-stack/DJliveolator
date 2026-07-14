using System;
using System.IO;
using Liveolator.Core.Settings;
using Liveolator.Visuals.Gl;
using SkiaSharp;
using Xunit;

namespace Liveolator.Visuals.Tests.Gl;

// Throwaway visual check: composes the static face with the needle drawn from the SAME geometry the
// shader uses (pivot per origin, dir flipped per origin), so the registration of needle-to-arc can be
// eyeballed without a GL context. Not an assertion of pixels — just a guard that the math lines up.
public sealed class VuMeterNeedleRegistrationTests
{
    [Theory]
    [InlineData(VuMeterNeedleOrigin.Bottom, 0.0)]
    [InlineData(VuMeterNeedleOrigin.Bottom, 0.78)]
    [InlineData(VuMeterNeedleOrigin.Top, 0.0)]
    [InlineData(VuMeterNeedleOrigin.Top, 0.78)]
    public void Compose_FaceWithNeedle_ForVisualInspection(VuMeterNeedleOrigin origin, double level)
    {
        using SKBitmap face = VuMeterFace.Render(origin);
        using var canvas = new SKCanvas(face);

        float px = VuMeterGeometry.PivotXPx;
        float py = VuMeterGeometry.PivotYPx(origin);
        float r = VuMeterGeometry.ArcRadiusPx;
        double ang = (VuMeterGeometry.NeedleMinDeg
            + (VuMeterGeometry.NeedleMaxDeg - VuMeterGeometry.NeedleMinDeg) * level) * Math.PI / 180.0;

        // Matches VuMeterAddon's shader: + = right; dirY = +cos (down, Top origin) or -cos (up, Bottom).
        float dirY = VuMeterGeometry.NeedleDown(origin) ? (float)Math.Cos(ang) : -(float)Math.Cos(ang);
        var dir = new SKPoint((float)Math.Sin(ang), dirY);
        var pivot = new SKPoint(px, py);
        var tip = new SKPoint(px + dir.X * (r + 12f), py + dir.Y * (r + 12f));
        var tail = new SKPoint(px - dir.X * 46f, py - dir.Y * 46f);

        using var needle = new SKPaint
        {
            IsAntialias = true, Color = new SKColor(0x0D, 0x0D, 0x0D),
            Style = SKPaintStyle.Stroke, StrokeWidth = 7, StrokeCap = SKStrokeCap.Round,
        };
        canvas.DrawLine(tail, tip, needle);

        string dir2 = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(dir2);
        string path = Path.Combine(dir2, $"vu-meter-needle-{origin}-{level:0.00}.png");
        using SKImage image = SKImage.FromBitmap(face);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
        using FileStream file = File.Create(path);
        data.SaveTo(file);

        Assert.True(new FileInfo(path).Length > 0);
    }
}
