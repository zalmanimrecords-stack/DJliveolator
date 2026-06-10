using Liveolator.Core.Visuals;
using SkiaSharp;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// SkiaSharp implementation of <see cref="IImageDimensionsProbe"/>: reads an image's pixel size from its
/// codec header (no full decode). Lives in the Visuals module because Skia is its image dependency; Core
/// only sees the seam. Best-effort — a missing/unreadable/non-image file returns false with (0, 0)
/// rather than throwing, so a caller advising on image size never crashes (global standards #16/#26).
/// </summary>
public sealed class SkiaImageDimensionsProbe : IImageDimensionsProbe
{
    /// <inheritdoc />
    public bool TryGetPixelSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            using SKCodec? codec = SKCodec.Create(path);
            if (codec is null)
                return false;

            width = codec.Info.Width;
            height = codec.Info.Height;
            return width > 0 && height > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
