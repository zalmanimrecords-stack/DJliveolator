using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Decodes a still image file to <see cref="RgbaImage"/> pixels via SkiaSharp (managed,
/// cross-platform). Used by the compositor to build the layer texture from a
/// <c>VisualSourceKind.Image</c> reference. A missing/undecodable file is surfaced as
/// <see cref="ImageLoadException"/> so the engine can degrade the layer gracefully (doc 08 error
/// handling) rather than crash the render loop.
/// </summary>
public sealed class SkiaImageLoader
{
    private readonly ILogger<SkiaImageLoader> _logger;

    public SkiaImageLoader(ILogger<SkiaImageLoader>? logger = null)
        => _logger = logger ?? NullLogger<SkiaImageLoader>.Instance;

    /// <summary>Loads and decodes <paramref name="filePath"/> into top-row-first RGBA8 pixels.</summary>
    /// <exception cref="ImageLoadException">The file is missing, unreadable, or not a decodable image.</exception>
    public RgbaImage Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        try
        {
            using var stream = File.OpenRead(filePath);
            using var codecData = SKData.Create(stream);
            // Decode straight into the GL-friendly layout: RGBA8888, unpremultiplied, top-row-first.
            using SKBitmap? bitmap = SKBitmap.Decode(codecData, NormalizedInfo(codecData));
            if (bitmap is null)
                throw new ImageLoadException($"'{filePath}' could not be decoded as an image.");

            byte[] pixels = bitmap.Bytes;
            return new RgbaImage(bitmap.Width, bitmap.Height, pixels).Validated();
        }
        catch (Exception ex) when (ex is not ImageLoadException)
        {
            _logger.LogWarning(ex, "Failed to load image texture from {FilePath}.", filePath);
            throw new ImageLoadException($"Failed to load image '{filePath}': {ex.Message}", ex);
        }
    }

    // Force the decode target to unpremultiplied RGBA8888 so the bytes are exactly what the GL
    // texture upload expects, independent of the source format's native color type.
    private static SKImageInfo NormalizedInfo(SKData encoded)
    {
        using var codec = SKCodec.Create(encoded);
        if (codec is null)
            throw new ImageLoadException("Unrecognized or unsupported image format.");
        return new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
    }
}
