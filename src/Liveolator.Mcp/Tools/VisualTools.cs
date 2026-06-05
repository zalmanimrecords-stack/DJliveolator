using System.ComponentModel;
using Liveolator.Core.Library.Visual;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>
/// MCP tools for cataloguing the performer's visual media (images + video clips), so an agent can
/// discover what footage exists and choose material to pair with music. (Generating new visuals
/// needs the visual engine — doc 08 — and is out of scope here.)
/// </summary>
[McpServerToolType]
public sealed class VisualTools
{
    [McpServerTool(Name = "scan_visual_folders")]
    [Description("Scan folders (recursively) for images and video clips and catalog each with its " +
                 "dimensions and (for video) duration. Incremental and cached. Images need no setup; " +
                 "video duration needs ffprobe (without it, videos still catalog with null duration).")]
    public static async Task<VisualScanSummary> ScanVisualFolders(
        VisualSession session,
        [Description("Absolute folder paths to scan, searched recursively.")] string[] folders,
        [Description("Re-probe every file even if already cached. Default false.")] bool force = false,
        CancellationToken cancellationToken = default)
        => await session.ScanAsync(folders, force, cancellationToken).ConfigureAwait(false);

    [McpServerTool(Name = "list_visuals")]
    [Description("List catalogued visual assets with optional filtering and paging — a way to see " +
                 "what footage is available to pair with tracks. Run scan_visual_folders first.")]
    public static async Task<IReadOnlyList<VisualAssetInfo>> ListVisuals(
        VisualSession session,
        [Description("Filter by kind: Image or Video. Omit for both.")] string? kind = null,
        [Description("Only assets at least this many pixels wide.")] int? minWidth = null,
        [Description("Max results to return. Default 100.")] int limit = 100,
        [Description("Number of results to skip (for paging). Default 0.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VisualAsset> all = await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<VisualAsset> query = all;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse(kind, ignoreCase: true, out VisualMediaKind parsed))
                throw new ArgumentException($"Unknown kind '{kind}'. Use Image or Video.");
            query = query.Where(a => a.Kind == parsed);
        }
        if (minWidth is { } w)
            query = query.Where(a => a.Info is { } info && info.Width >= w);

        return query
            .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(VisualAssetInfo.From)
            .ToList();
    }

    [McpServerTool(Name = "get_visual")]
    [Description("Get the catalogued metadata for one visual asset by its exact file path.")]
    public static async Task<VisualAssetInfo> GetVisual(
        VisualSession session,
        [Description("Exact file path of the visual asset (as catalogued).")] string path,
        CancellationToken cancellationToken = default)
    {
        VisualAsset? asset = await session.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (asset is null)
            throw new ArgumentException($"No catalogued visual asset at '{path}'. Scan its folder first, or check the path.");
        return VisualAssetInfo.From(asset);
    }
}
