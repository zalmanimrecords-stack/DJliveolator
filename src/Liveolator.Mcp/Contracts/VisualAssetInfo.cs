using Liveolator.Core.Library.Visual;

namespace Liveolator.Mcp.Contracts;

/// <summary>Stable, agent-facing view of one catalogued visual asset (image or video clip).</summary>
public sealed record VisualAssetInfo(
    string Path,
    string Title,
    string Kind,
    int? Width,
    int? Height,
    double? DurationSeconds,
    string Status,
    string? Error)
{
    public static VisualAssetInfo From(VisualAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new VisualAssetInfo(
            asset.File.Path,
            asset.Title,
            asset.Kind.ToString(),
            asset.Info?.Width,
            asset.Info?.Height,
            asset.Info?.Duration?.TotalSeconds,
            asset.Status.ToString(),
            asset.Error);
    }
}

/// <summary>Result of a visual-folder scan: catalog breakdown plus what this scan did. Video
/// duration needs ffprobe; without it, videos still catalog (duration null) — images always work.</summary>
public sealed record VisualScanSummary(
    int TotalAssets,
    int Images,
    int Videos,
    int Failed,
    int ProcessedThisScan,
    long ElapsedMs,
    IReadOnlyList<string> Folders,
    IReadOnlyList<FailedTrack> Failures);
