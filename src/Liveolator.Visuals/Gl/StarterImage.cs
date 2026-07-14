using SkiaSharp;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Generates a small placeholder image once, so the compositor's first slice has a renderable image
/// layer before a real scene catalog (doc 13) is wired. Written to a cache dir and reused if present.
/// </summary>
public static class StarterImage
{
    /// <summary>
    /// Ensures a starter PNG exists and returns its absolute path. Idempotent: regenerates only when
    /// missing. Throws <see cref="IOException"/>/Skia exceptions only on a genuine write failure —
    /// callers that want best-effort startup should guard the call.
    /// </summary>
    public static string EnsureCreated(string? directory = null)
    {
        directory ??= VisualAssetPaths.Default();
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "starter.png");
        if (File.Exists(path))
            return path;

        const int width = 1280, height = 720;
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using (var background = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(0, height),
                    new[] { new SKColor(0x10, 0x12, 0x18), new SKColor(0x24, 0x2d, 0x40) },
                    null, SKShaderTileMode.Clamp),
            })
            {
                canvas.DrawRect(new SKRect(0, 0, width, height), background);
            }

            using var title = new SKPaint
            {
                Color = new SKColor(0xE6, 0xEA, 0xF2),
                TextSize = 72,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                FakeBoldText = true,
            };
            canvas.DrawText("LIVEOLATOR", width / 2f, height / 2f, title);

            using var subtitle = new SKPaint
            {
                Color = new SKColor(0x6E, 0x9B, 0xFF),
                TextSize = 28,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
            };
            canvas.DrawText("visuals on the beat", width / 2f, height / 2f + 48, subtitle);
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
        return path;
    }
}
