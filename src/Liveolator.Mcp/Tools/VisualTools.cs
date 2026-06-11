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

    // --- FRKTL controllable presets (doc 28/29): an agent can author new generative visuals --------------

    [McpServerTool(Name = "get_visual_preset_spec")]
    [Description("Get the authoring contract for a FRKTL visual preset: the .frktl JSON format, the host " +
                 "shader uniforms available (uTime, uBeatPhase, uLevel, uBass..uHigh, uPreviousFrame for " +
                 "feedback, etc.), the rules (<=5 controllable parameters, ASCII-only shader), the folder " +
                 "presets are written to, and a complete worked example. ALWAYS call this before " +
                 "create_visual_preset so the generated shader and parameters are valid.")]
    public static VisualPresetSpec GetVisualPresetSpec(VisualPresetSession session)
        => session.Spec();

    [McpServerTool(Name = "create_visual_preset")]
    [Description("Create a FRKTL controllable visual preset from a complete .frktl JSON document and save " +
                 "it into the FRKTL presets folder, where the app picks it up. The JSON must follow the " +
                 "format from get_visual_preset_spec (name + up to 5 controllable parameters + a GLSL " +
                 "fragment shader). The preset is validated before writing; on failure nothing is written " +
                 "and the reason is returned in 'error'. The file name and preset id are derived from the " +
                 "name. Each controllable parameter becomes a knob the performer can turn or MIDI-map.")]
    public static VisualPresetResult CreateVisualPreset(
        VisualPresetSession session,
        [Description("The entire .frktl document as a JSON string (keys: name, author?, description?, " +
                     "parameters[], shader). See get_visual_preset_spec for the exact shape and an example.")]
        string presetJson,
        [Description("Overwrite an existing preset file with the same derived name. Default true.")]
        bool overwrite = true)
        => session.Create(presetJson, overwrite);

    [McpServerTool(Name = "list_visual_presets")]
    [Description("List the FRKTL presets currently installed in the presets folder (name + preset id + " +
                 "file path), so an agent can see what exists before creating or replacing one.")]
    public static IReadOnlyList<VisualPresetSummary> ListVisualPresets(VisualPresetSession session)
        => session.List();
}
