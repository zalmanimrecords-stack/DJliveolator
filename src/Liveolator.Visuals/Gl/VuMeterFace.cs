using SkiaSharp;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Renders the <b>static</b> analog VU-meter face — the black bezel + screws, the aged cream dial with
/// its dual-row scale, red zone, "VU METER" legend, and brass hub — to a PNG, faithfully matching the
/// reference meter. The moving needle is NOT drawn here; it is a transparent generator layer
/// (<see cref="VuMeterAddon"/>) composited over this face, so the needle reacts to the audio while the
/// face stays fixed. Geometry is shared via <see cref="VuMeterGeometry"/> so the needle aligns with the
/// printed arc. Skia is managed/cross-platform and runs headless (no GL), like <see cref="StarterImage"/>.
/// </summary>
public static class VuMeterFace
{
    // Bump when the drawing changes so an existing install regenerates the cached PNG.
    private const string Version = "v1";

    private static readonly SKColor Ink = new(0x1A, 0x17, 0x14);
    private static readonly SKColor Red = new(0xC0, 0x24, 0x1B);

    /// <summary>
    /// Ensures the face PNG exists and returns its absolute path. Idempotent per <see cref="Version"/>:
    /// regenerates only when missing. Throws on a genuine write failure — guard the call for best-effort startup.
    /// </summary>
    public static string EnsureCreated(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liveolator", "assets");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, $"vu-meter-face-{Version}.png");
        if (File.Exists(path))
            return path;

        using SKBitmap bitmap = Render();
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
        return path;
    }

    /// <summary>Renders the face to a bitmap (exposed so a test can inspect the result without GL).</summary>
    public static SKBitmap Render()
    {
        int w = VuMeterGeometry.FaceWidth;
        int h = VuMeterGeometry.FaceHeight;
        var bitmap = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bitmap);

        DrawBezel(canvas, w, h);
        DrawFace(canvas, w, h);
        DrawScale(canvas);
        DrawLegend(canvas, w, h);
        DrawHub(canvas);
        DrawScrews(canvas, w, h);

        return bitmap;
    }

    private static SKPoint PointAt(float cx, float cy, float radius, float angleDeg)
    {
        // Angle from straight DOWN, + toward the right: the hub is near the top and the scale arc + needle
        // fall BELOW it (top-pivot meter), so y grows with cos(angle).
        double rad = angleDeg * Math.PI / 180.0;
        return new SKPoint(
            (float)(cx + radius * Math.Sin(rad)),
            (float)(cy + radius * Math.Cos(rad)));
    }

    private static SKTypeface Serif() =>
        SKTypeface.FromFamilyName("Georgia")
        ?? SKTypeface.FromFamilyName("Times New Roman")
        ?? SKTypeface.Default;

    // ── Bezel + screws ─────────────────────────────────────────────────────────────────────────

    private static void DrawBezel(SKCanvas canvas, int w, int h)
    {
        canvas.Clear(new SKColor(0xCB, 0xC2, 0xB0)); // wall behind the unit

        var outer = new SKRect(10, 10, w - 10, h - 10);
        using (var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, outer.Top), new SKPoint(0, outer.Bottom),
            new[] { new SKColor(0x20, 0x1F, 0x1E), new SKColor(0x10, 0x0F, 0x0E) },
            null, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
        {
            canvas.DrawRoundRect(outer, 48, 48, paint);
        }

        // Subtle raised inner edge on the bezel.
        using (var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            Color = new SKColor(0x3A, 0x39, 0x37),
        })
        {
            var inset = new SKRect(outer.Left + 8, outer.Top + 8, outer.Right - 8, outer.Bottom - 8);
            canvas.DrawRoundRect(inset, 42, 42, edge);
        }
    }

    private static void DrawScrews(SKCanvas canvas, int w, int h)
    {
        float m = 58f;
        DrawScrew(canvas, m, m);
        DrawScrew(canvas, w - m, m);
        DrawScrew(canvas, m, h - m);
        DrawScrew(canvas, w - m, h - m);
        DrawScrew(canvas, w / 2f, h - 48f); // bottom-centre screw
    }

    private static void DrawScrew(SKCanvas canvas, float cx, float cy)
    {
        const float r = 27f;
        using (var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx - r * 0.3f, cy - r * 0.3f), r * 1.6f,
            new[] { new SKColor(0x52, 0x4A, 0x3A), new SKColor(0x17, 0x14, 0x10) },
            null, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
        {
            canvas.DrawCircle(cx, cy, r, paint);
        }
        using (var rim = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = new SKColor(0x0A, 0x09, 0x08),
        })
        {
            canvas.DrawCircle(cx, cy, r, rim);
        }
        // Phillips cross, slightly rotated.
        using var slot = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4,
            Color = new SKColor(0x0C, 0x0B, 0x0A), StrokeCap = SKStrokeCap.Round,
        };
        canvas.Save();
        canvas.RotateDegrees(18, cx, cy);
        canvas.DrawLine(cx - r * 0.55f, cy, cx + r * 0.55f, cy, slot);
        canvas.DrawLine(cx, cy - r * 0.55f, cx, cy + r * 0.55f, slot);
        canvas.Restore();
    }

    // ── Cream dial face ────────────────────────────────────────────────────────────────────────

    private static void DrawFace(SKCanvas canvas, int w, int h)
    {
        var window = new SKRect(w * 0.085f, h * 0.10f, w * 0.915f, h * 0.86f);

        // A thin dark rim around the cream window (the recessed glass frame).
        using (var rim = new SKPaint { IsAntialias = true, Color = new SKColor(0x05, 0x05, 0x05) })
            canvas.DrawRoundRect(new SKRect(window.Left - 8, window.Top - 8, window.Right + 8, window.Bottom + 8), 26, 26, rim);

        // Cream face with a warm vignette (lighter centre, darker tan toward the edges).
        using (var shader = SKShader.CreateRadialGradient(
            new SKPoint(window.MidX, window.MidY + h * 0.05f), window.Width * 0.72f,
            new[] { new SKColor(0xF2, 0xEB, 0xD8), new SKColor(0xE6, 0xDC, 0xC1), new SKColor(0xC9, 0xBB, 0x99) },
            new[] { 0f, 0.6f, 1f }, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
        {
            canvas.DrawRoundRect(window, 20, 20, paint);
        }

        // Very faint, broad aging — uniform patina rather than visible spots (matches the reference).
        using var stain = new SKPaint { IsAntialias = true };
        (float x, float y, float r)[] stains =
        {
            (window.Left + 140, window.Top + 120, 200),
            (window.Right - 150, window.Bottom - 130, 230),
            (window.MidX, window.Top + 40, 260),
        };
        foreach ((float x, float y, float r) in stains)
        {
            stain.Color = new SKColor(0x7A, 0x68, 0x44, 9);
            canvas.DrawCircle(x, y, r, stain);
        }
    }

    // ── Scale: arc, ticks, numbers ───────────────────────────────────────────────────────────────

    private static void DrawScale(SKCanvas canvas)
    {
        float cx = VuMeterGeometry.PivotXPx;
        float cy = VuMeterGeometry.PivotYPx;
        float r = VuMeterGeometry.ArcRadiusPx;

        // Arc line — black up to the redline, red beyond. Drawn as short segments along t.
        using var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        const int steps = 240;
        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;
            bool red = t0 >= VuMeterGeometry.RedlineT;
            arc.Color = red ? Red : Ink;
            arc.StrokeWidth = red ? 6f : 3.2f;
            canvas.DrawLine(
                PointAt(cx, cy, r, VuMeterGeometry.AngleDegAt(t0)),
                PointAt(cx, cy, r, VuMeterGeometry.AngleDegAt(t1)),
                arc);
        }

        // Ticks: a major at every labelled position, a minor between consecutive top labels.
        using var tick = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Butt };
        var top = VuMeterGeometry.TopLabels;
        for (int i = 0; i < top.Length; i++)
        {
            DrawTick(canvas, tick, cx, cy, r, top[i].T, major: true);
            if (i < top.Length - 1)
                DrawTick(canvas, tick, cx, cy, r, (top[i].T + top[i + 1].T) * 0.5f, major: false);
        }

        // Numbers: dB row above the arc, percentage row below it.
        using var font = new SKPaint
        {
            IsAntialias = true, Typeface = Serif(), TextAlign = SKTextAlign.Center, TextSize = 40,
        };
        foreach ((float t, string text) in top)
        {
            font.Color = t >= VuMeterGeometry.RedlineT ? Red : Ink;
            font.TextSize = (text is "-" or "+") ? 52 : 40;
            DrawLabel(canvas, font, cx, cy, r + 46f, t, text);
        }

        font.TextSize = 30;
        foreach ((float t, string text) in VuMeterGeometry.BottomLabels)
        {
            font.Color = text == "100" ? Red : Ink;
            DrawLabel(canvas, font, cx, cy, r - 40f, t, text);
        }
    }

    private static void DrawTick(SKCanvas canvas, SKPaint tick, float cx, float cy, float r, float t, bool major)
    {
        bool red = t >= VuMeterGeometry.RedlineT;
        tick.Color = red ? Red : Ink;
        tick.StrokeWidth = major ? 3.4f : 2f;
        float angle = VuMeterGeometry.AngleDegAt(t);
        float outer = r + (major ? 22f : 12f);
        canvas.DrawLine(PointAt(cx, cy, r, angle), PointAt(cx, cy, outer, angle), tick);
    }

    private static void DrawLabel(SKCanvas canvas, SKPaint font, float cx, float cy, float radius, float t, string text)
    {
        SKPoint p = PointAt(cx, cy, radius, VuMeterGeometry.AngleDegAt(t));
        // Centre the glyph vertically on the point (TextAlign handles horizontal).
        canvas.DrawText(text, p.X, p.Y + font.TextSize * 0.35f, font);
    }

    // ── Legend + hub ─────────────────────────────────────────────────────────────────────────────

    private static void DrawLegend(SKCanvas canvas, int w, int h)
    {
        // The hub is near the top, so the legend sits in the open band BELOW the scale arc.
        using var vu = new SKPaint
        {
            IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Georgia", SKFontStyle.Bold) ?? Serif(),
            TextAlign = SKTextAlign.Center, TextSize = 64, Color = Ink,
        };
        canvas.DrawText("VU", w / 2f, h * 0.78f, vu);

        using var meter = new SKPaint
        {
            IsAntialias = true, Typeface = Serif(), TextAlign = SKTextAlign.Center, TextSize = 28, Color = Ink,
        };
        DrawSpaced(canvas, meter, "METER", w / 2f, h * 0.84f, 12f);
    }

    private static void DrawSpaced(SKCanvas canvas, SKPaint font, string text, float centerX, float y, float tracking)
    {
        float[] widths = new float[text.Length];
        float total = 0;
        for (int i = 0; i < text.Length; i++)
        {
            widths[i] = font.MeasureText(text[i].ToString());
            total += widths[i] + (i < text.Length - 1 ? tracking : 0);
        }
        var left = new SKPaint
        {
            IsAntialias = true, Typeface = font.Typeface, TextSize = font.TextSize, Color = font.Color,
            TextAlign = SKTextAlign.Left,
        };
        float x = centerX - total / 2f;
        foreach (char c in text)
        {
            string s = c.ToString();
            canvas.DrawText(s, x, y, left);
            x += font.MeasureText(s) + tracking;
        }
        left.Dispose();
    }

    private static void DrawHub(SKCanvas canvas)
    {
        float cx = VuMeterGeometry.PivotXPx;
        float cy = VuMeterGeometry.PivotYPx;

        // Brass mechanism: a few concentric rings with a warm radial gradient.
        using (var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx - 6, cy - 6), 52,
            new[] { new SKColor(0xE8, 0xC8, 0x70), new SKColor(0xA7, 0x82, 0x3A), new SKColor(0x5E, 0x47, 0x1C) },
            new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
        {
            canvas.DrawCircle(cx, cy, 46, paint);
        }
        using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = new SKColor(0x4A, 0x37, 0x16) })
        {
            canvas.DrawCircle(cx, cy, 46, ring);
            canvas.DrawCircle(cx, cy, 36, ring);
            canvas.DrawCircle(cx, cy, 24, ring);
        }
        using (var cap = new SKPaint { IsAntialias = true, Color = new SKColor(0x2A, 0x20, 0x10) })
            canvas.DrawCircle(cx, cy, 13, cap);
    }
}
