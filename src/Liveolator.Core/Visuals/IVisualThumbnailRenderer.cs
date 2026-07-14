using Liveolator.Core.Library.Visual;

namespace Liveolator.Core.Visuals;

/// <summary>
/// Renders a still preview thumbnail for a visual asset — the decoded picture for an image, or a
/// single extracted frame for a video — as top-row-first RGBA8 pixels (reusing
/// <see cref="VisualPreviewFrame"/>). A Core seam: the native/FFmpeg implementations live in
/// Liveolator.Visuals and are wired at composition.
/// </summary>
/// <remarks>
/// Returns <c>null</c> when no preview can be produced (an unsupported/undecodable file, or a missing
/// external tool such as ffmpeg) so the UI degrades to a placeholder instead of failing — this is a
/// best-effort convenience, not a critical flow.
/// </remarks>
public interface IVisualThumbnailRenderer
{
    /// <param name="filePath">Absolute path to the asset's file.</param>
    /// <param name="kind">Image vs. video — selects the decode path.</param>
    /// <param name="maxEdge">Longest output edge in pixels; the frame is scaled down to fit, aspect kept.</param>
    Task<VisualPreviewFrame?> RenderAsync(
        string filePath, VisualMediaKind kind, int maxEdge, CancellationToken cancellationToken = default);
}
