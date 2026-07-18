using System.IO;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;

namespace Liveolator.Core.Enrichment;

/// <summary>Progress of an online enrichment pass.</summary>
/// <param name="Done">Tracks processed so far.</param>
/// <param name="Total">Tracks that had never been checked online when the pass started.</param>
/// <param name="Enriched">Of those processed, how many had online data (genre/BPM/key) applied.</param>
public readonly record struct EnrichmentProgress(int Done, int Total, int Enriched);

/// <summary>Result of a completed (or cancelled) enrichment pass.</summary>
/// <param name="Considered">Tracks that had never been checked online when the pass started.</param>
/// <param name="Enriched">How many tracks had online data applied.</param>
public readonly record struct EnrichmentOutcome(int Considered, int Enriched);

/// <summary>
/// Gives every catalogued track ONE online pass (doc 16): fills a missing genre and cross-checks the
/// locally detected BPM against the online value, applying each result through
/// <see cref="MusicLibrary.ApplyOnlineDetails"/> (agreement raises confidence; disagreement flags the
/// track <see cref="BpmProvenance.Conflicted"/> for review). Every COMPLETED lookup — hit or miss — is
/// stamped via <see cref="MusicLibrary.MarkOnlineLookup"/>, so a track is never re-queried on later
/// scans. Pure orchestration over the library + an optional catalog store — no UI, no native.
/// </summary>
/// <remarks>
/// Offline-first: a lookup that misses, throws, or returns null never stops the pass (global #16/#26);
/// a transport error leaves the track unstamped so the next pass retries it. GetSongBPM is a courtesy
/// free API, so calls are serialized with a small per-call delay (<see cref="_delay"/>, injectable so
/// tests run instantly). Each processed track is persisted individually through the incremental
/// <see cref="IMusicCatalogStore.SaveTrackAsync"/> seam — never a whole-catalog rewrite (doc 31 M1).
/// </remarks>
public sealed class CatalogEnrichmentService
{
    private readonly MusicLibrary _library;
    private readonly IMetadataProvider _provider;
    private readonly IMusicCatalogStore? _store;
    private readonly TimeSpan _delay;
    private readonly Action<string>? _onError;

    /// <param name="library">The catalog enriched in place.</param>
    /// <param name="provider">The online metadata source (required).</param>
    /// <param name="store">Persists each updated track; null skips persistence (in-memory only).</param>
    /// <param name="delay">Courtesy pause between online calls (rate-limit). Pass <see cref="TimeSpan.Zero"/> in tests.</param>
    /// <param name="onError">Receives a note for a single track/persist failure; the pass continues.</param>
    public CatalogEnrichmentService(
        MusicLibrary library, IMetadataProvider provider, IMusicCatalogStore? store = null,
        TimeSpan? delay = null, Action<string>? onError = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _store = store;
        _delay = delay ?? TimeSpan.FromMilliseconds(500);
        _onError = onError;
    }

    /// <summary>True when a track has never had a completed online lookup and is therefore a candidate.</summary>
    public static bool NeedsOnlineCheck(MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.OnlineLookupUtc is null;
    }

    /// <summary>
    /// Looks up each never-checked track online and applies the result, reporting progress and
    /// persisting each processed track. Honours cancellation between tracks; already-processed tracks
    /// were persisted individually, so a re-run resumes rather than repeats.
    /// </summary>
    public async Task<EnrichmentOutcome> RunAsync(
        IProgress<EnrichmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        List<MusicTrack> pending = _library.All.Where(NeedsOnlineCheck).ToList();
        int total = pending.Count;
        if (total == 0)
        {
            progress?.Report(new EnrichmentProgress(0, 0, 0));
            return new EnrichmentOutcome(0, 0);
        }

        int done = 0, enriched = 0;
        foreach (MusicTrack track in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool lookedUp = false;
            try
            {
                (bool applied, lookedUp) = await TryEnrichAsync(track, cancellationToken).ConfigureAwait(false);
                if (applied)
                    enriched++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One lookup/apply failure never stops the pass (global #16/#26).
                _onError?.Invoke($"Enriching '{track.File.Path}' failed: {ex.Message}");
            }

            if (lookedUp)
                await PersistTrackAsync(track.File.Path).ConfigureAwait(false);

            done++;
            progress?.Report(new EnrichmentProgress(done, total, enriched));

            // Courtesy rate-limit between online calls; skip the wait after the last track.
            if (done < total && _delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        }

        return new EnrichmentOutcome(total, enriched);
    }

    // Looks up one track and applies the online result. Returns (applied, lookedUp): lookedUp is true
    // for any COMPLETED lookup (hit or miss) — those are stamped so they are never re-queried; a track
    // we couldn't even build a query for stays eligible (no API call was spent on it).
    private async Task<(bool Applied, bool LookedUp)> TryEnrichAsync(MusicTrack track, CancellationToken cancellationToken)
    {
        (TrackLookupQuery query, bool identityFromTags) = BuildQuery(track);
        if (!query.HasTags)
            return (false, false);

        OnlineTrackMetadata? online = await _provider.LookupAsync(query, cancellationToken).ConfigureAwait(false);
        _library.MarkOnlineLookup(track.File.Path, DateTime.UtcNow);
        if (online is null)
            return (false, true);

        // A filename-guessed identity is too weak for a BPM verdict — an extended mix wrongly matched
        // to its radio edit paints a false conflict. Genre/key from such a match are still worth taking.
        if (!identityFromTags)
            online = online with { Bpm = null };

        return (_library.ApplyOnlineDetails(track.File.Path, online), true);
    }

    // Artist/title for the lookup: prefer tags, otherwise derive from the "Artist - Title" filename
    // convention. IdentityFromTags reports whether BOTH came from real tags (trusted for a BPM verdict).
    private static (TrackLookupQuery Query, bool IdentityFromTags) BuildQuery(MusicTrack track)
    {
        string? artist = track.Artist;
        string? title = string.IsNullOrWhiteSpace(track.Metadata?.Title) ? null : track.Metadata!.Title;
        bool identityFromTags = artist is not null && title is not null;

        if (!identityFromTags)
        {
            (string? fileArtist, string? fileTitle) = ParseFilename(track.File.Path);
            artist ??= fileArtist;
            title ??= fileTitle;
        }

        return (new TrackLookupQuery(Artist: artist, Title: title, Duration: track.Duration), identityFromTags);
    }

    // ponytail: minimal "Artist - Title" split (the common DJ filename convention); no fuzzy parser.
    private static (string? Artist, string? Title) ParseFilename(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int sep = name.IndexOf(" - ", StringComparison.Ordinal);
        if (sep < 0)
            return (null, null);

        string artist = name[..sep].Trim();
        string title = name[(sep + 3)..].Trim();
        return (
            string.IsNullOrWhiteSpace(artist) ? null : artist,
            string.IsNullOrWhiteSpace(title) ? null : title);
    }

    private async Task PersistTrackAsync(string path)
    {
        if (_store is null)
            return;

        MusicTrack? track = _library.TryGet(path);
        if (track is null)
            return;

        try
        {
            await _store.SaveTrackAsync(track, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _onError?.Invoke($"Saving enriched track '{path}' failed: {ex.Message}");
        }
    }
}
