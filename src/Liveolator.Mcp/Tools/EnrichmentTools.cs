using System.ComponentModel;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library.Music;
using Liveolator.Mcp.Contracts;
using Liveolator.Mcp.Session;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tools;

/// <summary>MCP tool for online BPM/key enrichment (doc 16) — a cross-check / fallback to local analysis.</summary>
[McpServerToolType]
public sealed class EnrichmentTools
{
    [McpServerTool(Name = "lookup_track_online")]
    [Description("Look up a track's BPM, key and genre from online sources as a cross-check or a " +
                 "fallback when local analysis has no value. Identifies the track by acoustic " +
                 "fingerprint (when a 'path' is given and Chromaprint/fpcalc is available) and queries " +
                 "GetSongBPM; otherwise matches by artist + title. Offline-first: returns Configured=false " +
                 "when no API key is set, and Found=false when nothing matches. The 'attribution' field " +
                 "MUST be shown wherever this data is displayed (GetSongBPM terms).")]
    public static async Task<OnlineLookupResult> LookupTrackOnline(
        IMetadataProvider provider,
        IAudioFingerprinter fingerprinter,
        LibrarySession session,
        [Description("Artist name. If omitted and 'path' is a catalogued track, the file's tag artist is used.")] string? artist = null,
        [Description("Track title. If omitted and 'path' is a catalogued track, the file's tag title is used.")] string? title = null,
        [Description("Optional file path; when given, an acoustic fingerprint is computed for more reliable identification.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        if (provider is DisabledMetadataProvider)
            return OnlineLookupResult.NotConfigured();

        // Fill missing artist/title from the catalogued track's tags when a path is supplied.
        if ((string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title)) && !string.IsNullOrWhiteSpace(path))
        {
            MusicTrack? track = await session.GetAsync(path, cancellationToken).ConfigureAwait(false);
            artist ??= track?.Artist;
            title ??= track?.Title;
        }

        // Prefer a fingerprint when we have the file (improves identification; degrades to null silently).
        AudioFingerprint? fingerprint = null;
        if (!string.IsNullOrWhiteSpace(path))
            fingerprint = await fingerprinter.ComputeAsync(path, cancellationToken).ConfigureAwait(false);

        var query = new TrackLookupQuery(
            artist,
            title,
            fingerprint?.Fingerprint,
            fingerprint is null ? null : TimeSpan.FromSeconds(fingerprint.DurationSeconds));

        if (!query.HasTags && !query.HasFingerprint)
            return OnlineLookupResult.NotFound("Provide artist + title, or a 'path' to a catalogued/fingerprintable file.");

        OnlineTrackMetadata? metadata = await provider.LookupAsync(query, cancellationToken).ConfigureAwait(false);
        return metadata is null
            ? OnlineLookupResult.NotFound("No online match for this track.")
            : OnlineLookupResult.From(metadata);
    }
}
