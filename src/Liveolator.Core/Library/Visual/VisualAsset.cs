using System.IO;

namespace Liveolator.Core.Library.Visual;

/// <summary>A catalogued visual media file (image or video clip) with probed metadata.</summary>
public sealed record VisualAsset(
    ScannedFile File,
    VisualMediaKind Kind,
    VisualMediaInfo? Info,
    MediaAnalysisStatus Status,
    string? Error) : IMediaEntry
{
    /// <summary>Display title derived from the file name.</summary>
    public string Title => Path.GetFileNameWithoutExtension(File.Path);
}
