namespace Liveolator.Core.Library.Visual;

/// <summary>Kind of visual media file.</summary>
public enum VisualMediaKind
{
    Image,
    Video
}

/// <summary>Probed visual metadata. <see cref="Duration"/> is null for still images.</summary>
public readonly record struct VisualMediaInfo(int Width, int Height, TimeSpan? Duration);

/// <summary>
/// Probe seam: reads dimensions/duration from a visual file. The real implementation
/// (FFmpeg/image decoder) lives in Liveolator.Visuals; Core depends only on this interface.
/// </summary>
public interface IVisualMediaProbe
{
    Task<VisualMediaInfo> ProbeAsync(string filePath, VisualMediaKind kind, CancellationToken cancellationToken = default);
}
