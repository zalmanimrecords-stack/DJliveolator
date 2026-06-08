using Liveolator.Core.Library.Visual;

namespace Liveolator.Core.Visuals.TrackPrograms;

/// <summary>Serializable reference and relinking fingerprint for an image or video asset.</summary>
public sealed record VisualAssetReference
{
    public VisualAssetReference(
        VisualMediaKind kind,
        string path,
        long sizeBytes,
        DateTime lastModifiedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        Kind = kind;
        Path = path;
        SizeBytes = sizeBytes;
        LastModifiedUtc = lastModifiedUtc;
    }

    public VisualMediaKind Kind { get; init; }
    public string Path { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastModifiedUtc { get; init; }
}
