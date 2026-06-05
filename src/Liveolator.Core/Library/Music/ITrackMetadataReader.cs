namespace Liveolator.Core.Library.Music;

/// <summary>
/// Reads tag/stream <see cref="TrackMetadata"/> from an audio file. The concrete reader lives
/// in a binding project (<c>Liveolator.Audio</c>) so <c>Core</c> stays free of file IO and
/// third-party tag libraries — the same seam pattern as <see cref="Analysis.IAudioDecoder"/>.
/// </summary>
public interface ITrackMetadataReader
{
    /// <summary>
    /// Reads metadata for <paramref name="filePath"/>, or returns <c>null</c> when the file has
    /// no readable tags or cannot be parsed. Implementations must never throw — a tag-read
    /// failure must not abort a library scan (failure isolation, global standards #16/#26).
    /// </summary>
    TrackMetadata? Read(string filePath);
}

/// <summary>No-op reader used when no metadata source is wired; always returns <c>null</c>.</summary>
public sealed class NullTrackMetadataReader : ITrackMetadataReader
{
    public static NullTrackMetadataReader Instance { get; } = new();

    public TrackMetadata? Read(string filePath) => null;
}
