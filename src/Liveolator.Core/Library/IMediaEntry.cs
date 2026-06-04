namespace Liveolator.Core.Library;

/// <summary>Outcome of building a library entry from a file.</summary>
public enum MediaAnalysisStatus
{
    Ok,
    Failed
}

/// <summary>
/// Common shape every library entry shares so the generic <see cref="MediaLibrary{TEntry}"/>
/// can track files, detect changes, and surface failures uniformly.
/// </summary>
public interface IMediaEntry
{
    ScannedFile File { get; }
    MediaAnalysisStatus Status { get; }
}
