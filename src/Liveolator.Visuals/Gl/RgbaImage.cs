namespace Liveolator.Visuals.Gl;

/// <summary>
/// A decoded still image as tightly-packed, top-row-first RGBA8 pixels — exactly the layout an
/// OpenGL texture upload expects (<c>GL_RGBA</c> / <c>GL_UNSIGNED_BYTE</c>). This is the bridge from
/// the existing image probe path to the GPU: the probe tells the catalog the file is an image; this
/// type turns that file into uploadable pixels for the layer texture (doc 08 image source slice).
/// </summary>
/// <param name="Width">Pixel width (> 0).</param>
/// <param name="Height">Pixel height (> 0).</param>
/// <param name="Pixels">Row-major RGBA bytes, length == Width * Height * 4.</param>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    /// <summary>Bytes per pixel for the RGBA8 layout.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>Validates the buffer matches the declared dimensions; the GL upload trusts this.</summary>
    public RgbaImage Validated()
    {
        if (Width <= 0 || Height <= 0)
            throw new ArgumentException($"Image dimensions must be positive (was {Width}x{Height}).");
        long expected = (long)Width * Height * BytesPerPixel;
        if (Pixels is null || Pixels.LongLength != expected)
            throw new ArgumentException(
                $"Pixel buffer length {Pixels?.LongLength ?? 0} does not match {Width}x{Height} RGBA ({expected}).");
        return this;
    }
}
