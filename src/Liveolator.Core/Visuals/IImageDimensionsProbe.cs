namespace Liveolator.Core.Visuals;

/// <summary>
/// Reads a still image's pixel dimensions without a GPU/GL context. The seam lives in Core so UI
/// view-models can advise on image sizing (doc 26 — the recommended VU-meter face size/aspect) while
/// staying off native code; the concrete implementation (SkiaSharp) lives in <c>Liveolator.Visuals</c>.
/// </summary>
public interface IImageDimensionsProbe
{
    /// <summary>
    /// Reads <paramref name="path"/>'s pixel size. Returns true with the dimensions on success, or false
    /// with (0, 0) when the file is missing/unreadable/not an image (best-effort, never throws).
    /// </summary>
    bool TryGetPixelSize(string path, out int width, out int height);
}
