using System.ComponentModel;
using Liveolator.Core.Library.Music;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>MCP tools for free-text discovery of catalogued tracks.</summary>
[McpServerToolType]
public sealed class SearchTools
{
    [McpServerTool(Name = "find_tracks")]
    [Description("Free-text search of the music catalog by title, artist, or file name " +
                 "(case-insensitive substring), with optional BPM-range and Camelot-key filters. " +
                 "Complements list_tracks, which filters/sorts but has no text search. Returns the full " +
                 "analysis for each match, ordered by title. Run scan_music_folders first to populate " +
                 "the catalog.")]
    public static async Task<IReadOnlyList<TrackInfo>> FindTracks(
        LibrarySession session,
        [Description("Text to match in the track title, artist, or file name. Omit to match all.")] string? text = null,
        [Description("Only tracks with BPM ≥ this value.")] double? minBpm = null,
        [Description("Only tracks with BPM ≤ this value.")] double? maxBpm = null,
        [Description("Only tracks in this Camelot key (e.g. '8B').")] string? camelot = null,
        [Description("Max results to return. Default 100.")] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MusicTrack> all = await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        return TrackQuery.Search(all, text, minBpm, maxBpm, camelot, limit)
            .Select(TrackInfo.From)
            .ToList();
    }
}
