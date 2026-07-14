using System.IO;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;

namespace Liveolator.Core.Enrichment;

/// <summary>Progress of an online genre-enrichment pass.</summary>
/// <param name="Done">Tracks processed so far.</param>
/// <param name="Total">Tracks that were missing genre when the pass started.</param>
/// <param name="Enriched">Of those processed, how many had a genre filled in from the online provider.</param>
public readonly record struct EnrichmentProgress(int Done, int Total, int Enriched);

/// <summary>Result of a completed (or cancelled) enrichment pass.</summary>
/// <param name="Considered">Tracks that were missing genre when the pass started.</param>
/// <param name="Enriched">How many genres were filled in.</param>
public readonly record struct EnrichmentOutcome(int Considered, int Enriched);

/// <summary>
/// Fills in missing track genre (and any BPM/key the merge policy accepts) from an online
/// <see cref="IMetadataProvider"/>, applying each result through <see cref="MusicLibrary.ApplyOnlineDetails"/>
/// (doc 16). Pure orchestration over the library + an optional catalog store — no UI, no native.
/// </summary>
/// <remarks>
/// Offline-first: a lookup that misses, throws, or returns null never stops the pass (global #16/#26).
/// GetSongBPM is a courtesy free API, so calls are serialized with a small per-call delay
/// (<see cref="_delay"/>, injectable so tests run instantly).
/// </remarks>
public sealed class CatalogEnrichmentService
{
    private readonly MusicLibrary _library;
    private readonly IMetadataProvider _provider;
    private readonly IMusicCatalogStore? _store;
    private readonly TimeSpan _delay;
    private readonly int _persistEvery;
    private readonly Action<string>? _onError;

    /// <param name="library">The catalog whose missing genres are filled in place.</param>
    /// <param name="provider">The online metadata source (required).</param>
    /// <param name="store">Persists the updated catalog; null skips persistence (in-memory only).</param>
    /// <param name="delay">Courtesy pause between online calls (rate-limit). Pass <see cref="TimeSpan.Zero"/> in tests.</param>
    /// <param name="persistEvery">Save the catalog every N processed tracks, bounding lost work on a crash.</param>
    /// <param name="onError">Receives a note for a single track/persist failure; the pass continues.</param>
    public CatalogEnrichmentService(
        MusicLibrary library, IMetadataProvider provider, IMusicCatalogStore? store = null,
        TimeSpan? delay = null, int persistEvery = 25, Action<string>? onError = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _store = store;
        _delay = delay ?? TimeSpan.FromMilliseconds(500);
        _persistEvery = persistEvery > 0 ? persistEvery : 1;
        _onError = onError;
    }

    /// <summary>True when a track has no usable genre tag and is therefore a candidate for enrichment.</summary>
    public static bool NeedsGenre(MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return string.IsNullOrWhiteSpace(track.Metadata?.Genre);
    }

    /// <summary>
    /// Looks up each genre-less track online and applies the result, reporting progress and persisting
    /// periodically. Honours cancellation between tracks; on cancel it persists progress already made
    /// before rethrowing, so a re-run resumes rather than repeats.
    /// </summary>
    public async Task<EnrichmentOutcome> RunAsync(
        IProgress<EnrichmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        List<MusicTrack> pending = _library.All.Where(NeedsGenre).ToList();
        int total = pending.Count;
        if (total == 0)
        {
            progress?.Report(new EnrichmentProgress(0, 0, 0));
            return new EnrichmentOutcome(0, 0);
        }

        int done = 0, enriched = 0;
        bool dirtySincePersist = false;
        try
        {
            foreach (MusicTrack track in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await TryEnrichAsync(track, cancellationToken).ConfigureAwait(false))
                    {
                        enriched++;
                        dirtySincePersist = true;
                    }
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

                done++;
                progress?.Report(new EnrichmentProgress(done, total, enriched));

                if (dirtySincePersist && done % _persistEvery == 0)
                {
                    await PersistAsync().ConfigureAwait(false);
                    dirtySincePersist = false;
                }

                // Courtesy rate-limit between online calls; skip the wait after the last track.
                if (done < total && _delay > TimeSpan.Zero)
                    await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (dirtySincePersist)
                await PersistAsync().ConfigureAwait(false);
        }

        return new EnrichmentOutcome(total, enriched);
    }

    // Looks up one track and applies any online genre. Returns true only when a genre was actually filled in.
    private async Task<bool> TryEnrichAsync(MusicTrack track, CancellationToken cancellationToken)
    {
        TrackLookupQuery query = BuildQuery(track);
        if (!query.HasTags)
            return false;

        OnlineTrackMetadata? online = await _provider.LookupAsync(query, cancellationToken).ConfigureAwait(false);
        if (online is null || string.IsNullOrWhiteSpace(online.Genre))
            return false;

        return _library.ApplyOnlineDetails(track.File.Path, online);
    }

    // Artist/title for the lookup: prefer tags, otherwise derive from the "Artist - Title" filename
    // convention (no dedicated parser exists in the codebase, so a minimal " - " split is used).
    private static TrackLookupQuery BuildQuery(MusicTrack track)
    {
        string? artist = track.Artist;
        string? title = string.IsNullOrWhiteSpace(track.Metadata?.Title) ? null : track.Metadata!.Title;

        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            (string? fileArtist, string? fileTitle) = ParseFilename(track.File.Path);
            artist ??= fileArtist;
            title ??= fileTitle;
        }

        return new TrackLookupQuery(Artist: artist, Title: title, Duration: track.Duration);
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

    private async Task PersistAsync()
    {
        if (_store is null)
            return;
        try
        {
            await _store.SaveMusicAsync(_library.All, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _onError?.Invoke($"Saving the enriched catalog failed: {ex.Message}");
        }
    }
}
