namespace Liveolator.Core.Enrichment;

/// <summary>
/// Looks up online metadata (BPM/key/genre cross-check) for a track (doc 16). The concrete provider —
/// acoustic-fingerprint identification (AcoustID → MusicBrainz) plus a tempo source (GetSongBPM) —
/// lives in a binding project; Core depends only on this seam, so the enrichment logic unit-tests with
/// a fake and no network.
/// </summary>
/// <remarks>
/// <b>Offline-first</b> (Core iron rule + doc 16): enrichment is optional. A miss, a network error, a
/// missing API key, or a rate-limit must resolve to <c>null</c> — never an exception — so a failed
/// lookup never degrades the local analysis the app already has. Implementations are expected to cache
/// and rate-limit, and to read API keys from config (never hardcoded — global standard #17).
/// </remarks>
public interface IMetadataProvider
{
    /// <summary>
    /// Looks up online metadata for the track described by <paramref name="query"/>, or returns
    /// <c>null</c> when nothing is found or the lookup cannot complete. Never throws for "not found"
    /// or transport failures.
    /// </summary>
    Task<OnlineTrackMetadata?> LookupAsync(TrackLookupQuery query, CancellationToken cancellationToken = default);
}
