namespace Liveolator.Core.Visuals.TrackPrograms;

/// <summary>
/// Serializable identity and relinking hints for the music track owned by a visual program.
/// The path is authoritative in the first schema; the fingerprint supports validation and relinking.
/// </summary>
public sealed record TrackReference
{
    public TrackReference(
        string path,
        long sizeBytes,
        DateTime lastModifiedUtc,
        string? artist,
        string? title,
        TimeSpan? duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        Path = path;
        SizeBytes = sizeBytes;
        LastModifiedUtc = lastModifiedUtc;
        Artist = artist;
        Title = title;
        Duration = duration;
    }

    public string Path { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastModifiedUtc { get; init; }
    public string? Artist { get; init; }
    public string? Title { get; init; }
    public TimeSpan? Duration { get; init; }
}
