namespace Liveolator.Mcp.Contracts;

/// <summary>A file that could not be analyzed, with the reason.</summary>
public sealed record FailedTrack(string Path, string Error);

/// <summary>
/// Result of a folder scan: the resulting catalog breakdown plus what this scan did. When the
/// FFmpeg native libraries are missing, compressed formats (mp3/flac/…) appear in
/// <see cref="Failures"/> with an actionable message — the agent gets a clear picture of any gap
/// rather than a silent omission. WAV files always analyze (no native dependency).
/// </summary>
public sealed record ScanSummary(
    int TotalTracks,
    int Ok,
    int PartiallyAnalyzed,
    int Failed,
    int ProcessedThisScan,
    long ElapsedMs,
    IReadOnlyList<string> Folders,
    IReadOnlyList<FailedTrack> Failures);

/// <summary>Outcome of re-analyzing stale or incomplete catalog entries.</summary>
public sealed record ReanalysisSummary(
    int Considered,
    int Analyzed,
    int Remaining,
    IReadOnlyList<FailedTrack> Failures);
