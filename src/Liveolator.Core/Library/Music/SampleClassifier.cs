namespace Liveolator.Core.Library.Music;

/// <summary>
/// Decides whether an audio file is a <see cref="MusicMediaKind.Sample"/> or a full
/// <see cref="MusicMediaKind.Track"/> — the hybrid rule: a file under a user-designated "samples"
/// folder is always a Sample (the override); otherwise a file shorter than a threshold (default 30s)
/// is a Sample, and everything else (including unknown-duration files) is a Track. Pure and IO-free.
/// </summary>
public static class SampleClassifier
{
    /// <summary>Default max length for the duration heuristic: shorter ⇒ Sample.</summary>
    public static readonly TimeSpan DefaultMaxSampleLength = TimeSpan.FromSeconds(30);

    public static MusicMediaKind Classify(
        string filePath,
        TimeSpan? duration,
        IReadOnlySet<string> sampleFolders,
        TimeSpan? maxSampleLength = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(sampleFolders);

        if (IsUnderAnySampleFolder(filePath, sampleFolders))
            return MusicMediaKind.Sample;

        TimeSpan threshold = maxSampleLength ?? DefaultMaxSampleLength;
        // Unknown duration → treat as a full track (don't guess "sample" without evidence).
        return duration is { } d && d < threshold ? MusicMediaKind.Sample : MusicMediaKind.Track;
    }

    private static bool IsUnderAnySampleFolder(string filePath, IReadOnlySet<string> sampleFolders)
    {
        if (sampleFolders.Count == 0)
            return false;
        string normalizedFile = FolderScope.Normalize(filePath);
        foreach (string folder in sampleFolders)
        {
            if (string.IsNullOrWhiteSpace(folder))
                continue;
            if (FolderScope.IsUnderNormalized(normalizedFile, FolderScope.Normalize(folder)))
                return true;
        }
        return false;
    }
}
