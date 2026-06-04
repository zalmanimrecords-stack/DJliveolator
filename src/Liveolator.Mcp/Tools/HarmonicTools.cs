using System.ComponentModel;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library.Music;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>MCP tools for harmonic (Camelot-wheel) mixing suggestions.</summary>
[McpServerToolType]
public sealed class HarmonicTools
{
    [McpServerTool(Name = "harmonic_matches")]
    [Description("Given a catalogued seed track, list other catalogued tracks that are harmonically " +
                 "compatible to mix into (same key, relative major/minor, or an adjacent Camelot key). " +
                 "Each match includes how it relates to the seed.")]
    public static async Task<IReadOnlyList<HarmonicMatch>> HarmonicMatches(
        LibrarySession session,
        [Description("Exact file path of the seed track (must be catalogued).")] string path,
        [Description("Max matches to return. Default 25.")] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await session.HarmonicMatchesAsync(path, cancellationToken).ConfigureAwait(false);
        if (result is null)
            throw new ArgumentException($"No catalogued track at '{path}'. Scan its folder first, or check the path.");

        (MusicTrack seed, IReadOnlyList<MusicTrack> matches) = result.Value;
        string seedCamelot = seed.Key!.Camelot;

        return matches
            .Take(Math.Clamp(limit, 1, 500))
            .Select(m => new HarmonicMatch(TrackInfo.From(m), CamelotRelationship.Describe(seedCamelot, m.Key!.Camelot)))
            .ToList();
    }

    [McpServerTool(Name = "compatible_keys")]
    [Description("List the Camelot keys that mix harmonically with a given Camelot code (e.g. '8B' → " +
                 "8B, 8A, 7B, 9B). Pure music theory — no catalog needed.")]
    public static IReadOnlyList<string> CompatibleKeys(
        [Description("A Camelot code such as '8B' or '12A'.")] string camelot)
    {
        ArgumentException.ThrowIfNullOrEmpty(camelot);
        var compatible = CamelotRelationship.AllCodes()
            .Where(code => Camelot.IsCompatible(camelot, code))
            .ToList();
        if (compatible.Count == 0)
            throw new ArgumentException($"'{camelot}' is not a valid Camelot code (use 1A–12A or 1B–12B).");
        return compatible;
    }
}
