namespace Liveolator.Mcp.Contracts;

/// <summary>A file that could not be analyzed, with the reason.</summary>
public sealed record FailedTrack(string Path, string Error);

/// <summary>
/// Result of a folder scan: the resulting catalog breakdown plus what this scan did. When the
/// FFmpeg native libraries are missing, compressed formats (mp3/flac/…) appear in
/// <see cref="Failures"/> with an actionable message — the agent gets a clear picture of any gap
/// rather than a silent omission. WAV files always analyze (no native dependency).
/// </summary>
/// <param name="Folders">The folders this scan actually walked — the ones that were requested. Previously
/// this echoed every folder ever scanned into the data root, which read as though all of them had been
/// re-walked (issue #3); that set is now reported separately as <paramref name="KnownFolders"/>.</param>
/// <param name="KnownFolders">Every folder catalogued in this data root, whether or not this scan
/// walked it. Context for what else the catalog holds, not a record of work done.</param>
public sealed record ScanSummary(
    int TotalTracks,
    int Ok,
    int PartiallyAnalyzed,
    int Failed,
    int ProcessedThisScan,
    long ElapsedMs,
    IReadOnlyList<string> Folders,
    IReadOnlyList<string> KnownFolders,
    IReadOnlyList<FailedTrack> Failures);

/// <summary>Outcome of re-analyzing stale or incomplete catalog entries.</summary>
public sealed record ReanalysisSummary(
    int Considered,
    int Analyzed,
    int Remaining,
    IReadOnlyList<FailedTrack> Failures);
