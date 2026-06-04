using System.ComponentModel;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using Liveolator.Media;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>MCP tools for generating and exporting harmonically-coherent playlists.</summary>
[McpServerToolType]
public sealed class PlaylistTools
{
    private static readonly HarmonicSetBuilder Builder = new();

    [McpServerTool(Name = "build_harmonic_playlist")]
    [Description("Build a harmonically-coherent set starting from a seed track, chaining " +
                 "Camelot-compatible tracks from the catalog with a smooth tempo progression. The " +
                 "tempo 'trend' controls energy: Any, Steady, Rising (build up), or Falling (wind down). " +
                 "Each step explains its key relationship and BPM change.")]
    public static async Task<PlaylistResult> BuildHarmonicPlaylist(
        LibrarySession session,
        [Description("Exact file path of the seed track (must be catalogued and have a detected key).")] string seedPath,
        [Description("Total number of tracks including the seed. Default 8.")] int length = 8,
        [Description("Max tempo change per step in BPM. Default 6.")] double bpmTolerance = 6.0,
        [Description("Tempo trend: Any, Steady, Rising, or Falling. Default Any.")] string trend = "Any",
        CancellationToken cancellationToken = default)
    {
        MusicTrack? seed = await session.GetAsync(seedPath, cancellationToken).ConfigureAwait(false);
        if (seed is null)
            throw new ArgumentException($"No catalogued track at '{seedPath}'. Scan its folder first, or check the path.");
        if (!Enum.TryParse(trend, ignoreCase: true, out BpmTrend parsedTrend))
            throw new ArgumentException($"Unknown trend '{trend}'. Use Any, Steady, Rising, or Falling.");

        IReadOnlyList<MusicTrack> candidates = await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        HarmonicSet set = Builder.Build(seed, candidates, new HarmonicSetOptions(length, bpmTolerance, parsedTrend));

        var steps = set.Entries
            .Select(e => new PlaylistStep(TrackInfo.From(e.Track), e.Rationale?.Relationship, e.Rationale?.BpmDelta))
            .ToList();
        double totalSeconds = set.Entries.Sum(e => e.Track.Duration?.TotalSeconds ?? 0);

        return new PlaylistResult(steps, set.Count, Math.Round(totalSeconds, 1));
    }

    [McpServerTool(Name = "export_playlist")]
    [Description("Write an ordered list of catalogued tracks to a playlist file. Format 'm3u8' " +
                 "produces a standard player playlist; 'json' includes per-track analysis. All paths " +
                 "must be catalogued.")]
    public static async Task<PlaylistExportResult> ExportPlaylist(
        LibrarySession session,
        PlaylistWriter writer,
        [Description("Ordered file paths of catalogued tracks to include.")] string[] trackPaths,
        [Description("Output format: 'm3u8' or 'json'.")] string format,
        [Description("Absolute destination file path.")] string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackPaths);
        if (trackPaths.Length == 0)
            throw new ArgumentException("Provide at least one track path.", nameof(trackPaths));
        if (!Enum.TryParse(format, ignoreCase: true, out PlaylistFormat parsedFormat))
            throw new ArgumentException($"Unknown format '{format}'. Use m3u8 or json.");

        var tracks = new List<MusicTrack>(trackPaths.Length);
        var missing = new List<string>();
        foreach (string path in trackPaths)
        {
            MusicTrack? track = await session.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (track is null) missing.Add(path);
            else tracks.Add(track);
        }
        if (missing.Count > 0)
            throw new ArgumentException($"These paths are not catalogued: {string.Join(", ", missing)}. Scan their folders first.");

        await writer.WriteAsync(tracks, outputPath, parsedFormat, cancellationToken).ConfigureAwait(false);
        return new PlaylistExportResult(outputPath, tracks.Count, parsedFormat.ToString());
    }
}
