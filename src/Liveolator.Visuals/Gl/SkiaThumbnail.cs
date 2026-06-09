using Liveolator.Core.Visuals;
using SkiaSharp;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Decodes an encoded image (file bytes or an ffmpeg-extracted PNG frame) into a downscaled
/// <see cref="VisualPreviewFrame"/> for the in-app preview. Shared by the image and video thumbnail
/// renderers so both reach the preview through one tested Skia decode + scale path. Output is the same
/// GL/Avalonia-friendly layout the rest of the engine uses: top-row-first, unpremultiplied RGBA8888.
/// </summary>
internal static class SkiaThumbnail
{
    /// <summary>Decodes <paramref name="encoded"/> and scales it so its longest edge is at most
    /// <paramref name="maxEdge"/> (never upscaled). Throws <see cref="ImageLoadException"/> if the
    /// bytes are not a decodable image.</summary>
    public static VisualPreviewFrame DecodeScaled(Stream encoded, int maxEdge)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        using SKBitmap? decoded = SKBitmap.Decode(encoded);
        if (decoded is null)
            throw new ImageLoadException("The data could not be decoded as an image.");

        (int width, int height) = TargetSize(decoded.Width, decoded.Height, maxEdge);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        using SKBitmap? scaled = decoded.Resize(info, SKFilterQuality.Medium);
        if (scaled is null)
            throw new ImageLoadException("The decoded image could not be resized for preview.");

        return new VisualPreviewFrame(scaled.Width, scaled.Height, scaled.Bytes);
    }

    // Fit within maxEdge on the longest side, preserving aspect; never enlarge a small source.
    private static (int Width, int Height) TargetSize(int width, int height, int maxEdge)
    {
        if (width <= 0 || height <= 0)
            return (1, 1);

        int longest = Math.Max(width, height);
        if (maxEdge <= 0 || longest <= maxEdge)
            return (width, height);

        double scale = (double)maxEdge / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
