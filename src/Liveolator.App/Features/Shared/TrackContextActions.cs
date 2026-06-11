using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using Liveolator.Core.Actions;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using ReactiveUI;

namespace Liveolator.App.Features.Shared;

/// <summary>
/// The shared back-end for the per-track right-click menu (Add to Deck A/B, Add to playlist).
/// One DI singleton drives every track row's <see cref="TrackMenuViewModel"/>. Deck loads go through
/// the <see cref="IPerformanceActionDispatcher"/> (doc 04 seam — never a direct engine call);
/// playlist edits go through <see cref="IPlaylistStore"/>. Deck availability is read from the
/// dispatcher feedback seam, so deck items disable when a deck isn't backed and the whole deck section
/// disables in catalog-browser mode (no dispatcher) — never a silent failure (global #16/#26).
/// </summary>
public sealed class TrackContextActions
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IPlaylistStore _store;
    private readonly Action<string>? _onStatus;
    private readonly MusicLibrary? _library;
    private readonly IMusicCatalogStore? _catalogStore;
    private readonly IMetadataProvider? _metadataProvider;
    private readonly IAudioFingerprinter? _fingerprinter;
    private readonly ITrackEditor? _editor;
    private readonly DeckTrackLoader? _deckLoader;

    public TrackContextActions(
        IPerformanceActionDispatcher? dispatcher,
        IPlaylistStore store,
        Action<string>? onStatus = null,
        MusicLibrary? library = null,
        IMusicCatalogStore? catalogStore = null,
        IMetadataProvider? metadataProvider = null,
        IAudioFingerprinter? fingerprinter = null,
        ITrackEditor? editor = null,
        DeckTrackLoader? deckLoader = null)
    {
        _dispatcher = dispatcher;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _onStatus = onStatus;
        _library = library;
        _catalogStore = catalogStore;
        _metadataProvider = metadataProvider;
        _fingerprinter = fingerprinter;
        _editor = editor;
        // The shared load-or-queue policy (doc 09/11): file-reachability check + never cut off a
        // playing deck. A custom loader is injected by tests; the default probes the real filesystem.
        _deckLoader = deckLoader
            ?? (dispatcher is null ? null : new DeckTrackLoader(dispatcher, System.IO.File.Exists));

        CanLoadToDeckA = DeckSlotAvailable(0);
        CanLoadToDeckB = DeckSlotAvailable(1);
    }

    /// <summary>Saved set names for the "Add to playlist" submenu; refreshed from the store.</summary>
    public ObservableCollection<string> Playlists { get; } = new();

    /// <summary>True when deck slot A / B is backed by the engine (drives the deck menu items).</summary>
    public bool CanLoadToDeckA { get; }
    public bool CanLoadToDeckB { get; }
    public bool CanAnalyze => _library is not null && _catalogStore is not null;
    public bool CanEdit => CanAnalyze && _editor is not null;
    public event EventHandler<string>? TrackChanged;
    public event EventHandler<string>? StatusChanged;

    public async Task AnalyzeAgainAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        if (!CanAnalyze || string.IsNullOrWhiteSpace(trackPath))
            return;

        try
        {
            ReportStatus($"Analyzing \"{TitleOf(trackPath)}\"...");
            await _library!.ForceReanalyzeAsync(trackPath, cancellationToken).ConfigureAwait(false);
            MusicTrack? track = _library.TryGet(trackPath);
            string onlineStatus = "online lookup not configured";

            if (track is not null && _metadataProvider is not null)
            {
                AudioFingerprint? fingerprint = _fingerprinter is null
                    ? null
                    : await _fingerprinter.ComputeAsync(trackPath, cancellationToken).ConfigureAwait(false);
                OnlineTrackMetadata? online = await _metadataProvider.LookupAsync(
                    new TrackLookupQuery(
                        track.Artist,
                        track.Title,
                        fingerprint?.Fingerprint,
                        track.Duration),
                    cancellationToken).ConfigureAwait(false);
                if (online is not null)
                {
                    _library.ApplyOnlineDetails(trackPath, online);
                    onlineStatus = $"checked {online.Source}";
                }
                else
                {
                    onlineStatus = "no online match";
                }
            }

            await PersistCatalogAsync(cancellationToken).ConfigureAwait(false);
            RaiseTrackChanged(trackPath);
            MusicTrack? updated = _library.TryGet(trackPath);
            ReportStatus(updated is null
                ? $"Could not find \"{TitleOf(trackPath)}\" in the catalog."
                : $"Analyzed \"{updated.Title}\": {updated.Bpm?.Bpm:0.0} BPM, " +
                  $"{updated.Key?.Camelot ?? "no key"}, {onlineStatus}.");
        }
        catch (Exception ex)
        {
            ReportStatus($"Could not analyze \"{TitleOf(trackPath)}\": {ex.Message}");
        }
    }

    public async Task EditAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        if (!CanEdit || _library!.TryGet(trackPath) is not { } track)
            return;

        TrackEditResult? edit = await _editor!.EditAsync(track);
        if (edit is null)
            return;

        try
        {
            _library.UpdateManualDetails(
                trackPath, edit.Bpm, edit.Camelot, edit.Genre, edit.Notes);
            await PersistCatalogAsync(cancellationToken).ConfigureAwait(false);
            RaiseTrackChanged(trackPath);
            ReportStatus($"Saved manual metadata for \"{track.Title}\".");
        }
        catch (Exception ex)
        {
            ReportStatus($"Could not save track metadata: {ex.Message}");
        }
    }

    /// <summary>
    /// Stages a track on a deck slot (A = 0, B = 1) without auto-playing it — unless that deck is
    /// playing, in which case the track is appended to the deck's live queue instead (a load never
    /// cuts off the floor's audio). An unreachable file (missing / offline drive) dispatches nothing
    /// and reports why. <paramref name="bpm"/> is the track's analyzed tempo (0 = unknown), fed to the
    /// deck as its Sync reference (doc 11); <paramref name="firstBeatSeconds"/> is the analyzed
    /// downbeat anchor (0 = unknown), fed to phase-match (doc 22 A1) right after the load.
    /// </summary>
    public void LoadToDeck(int slot, string trackPath, double bpm, double firstBeatSeconds = 0)
    {
        if (_deckLoader is null || string.IsNullOrWhiteSpace(trackPath))
            return;
        DeckLoadResult result = _deckLoader.Load(slot, trackPath, bpm, firstBeatSeconds);
        ReportStatus(result.Message);
    }

    /// <summary>Reloads the saved-set names (call at startup and after a set is created/changed).</summary>
    public async Task RefreshPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<string> names = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                Playlists.Clear();
                foreach (string name in names)
                    Playlists.Add(name);
            });
        }
        catch (Exception ex)
        {
            _onStatus?.Invoke($"Could not list playlists: {ex.Message}");
        }
    }

    /// <summary>Appends a track to a saved set (no duplicates), creating nothing — the set must exist.</summary>
    public async Task AddToPlaylistAsync(string trackPath, string playlistName, CancellationToken cancellationToken = default)
    {
        try
        {
            Playlist? playlist = await _store.LoadAsync(playlistName, cancellationToken).ConfigureAwait(false);
            playlist ??= Playlist.Empty(playlistName);

            if (playlist.TrackPaths.Any(p => string.Equals(p, trackPath, StringComparison.OrdinalIgnoreCase)))
            {
                _onStatus?.Invoke($"\"{TitleOf(trackPath)}\" is already in \"{playlistName}\".");
                return;
            }

            var updated = playlist.WithTracks(playlist.TrackPaths.Append(trackPath));
            await _store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            _onStatus?.Invoke($"Added \"{TitleOf(trackPath)}\" to \"{playlistName}\".");
        }
        catch (Exception ex)
        {
            _onStatus?.Invoke($"Could not add to \"{playlistName}\": {ex.Message}");
        }
    }

    /// <summary>Creates a new, uniquely-named set containing just this track, then saves it.</summary>
    public async Task AddToNewPlaylistAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        try
        {
            string name = await NextNewSetNameAsync(cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(new Playlist(name, new[] { trackPath }), cancellationToken).ConfigureAwait(false);
            await RefreshPlaylistsAsync(cancellationToken).ConfigureAwait(false);
            _onStatus?.Invoke($"Created \"{name}\" with \"{TitleOf(trackPath)}\".");
        }
        catch (Exception ex)
        {
            _onStatus?.Invoke($"Could not create a new set: {ex.Message}");
        }
    }

    private async Task<string> NextNewSetNameAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> existing = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        const string baseName = "New set";
        if (!taken.Contains(baseName))
            return baseName;
        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName} ({i})";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    private bool DeckSlotAvailable(int slot)
        => _dispatcher?.GetFeedback(PerformanceActionKind.DeckPlayPause, slot).IsAvailable ?? false;

    private static string TitleOf(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

    private Task PersistCatalogAsync(CancellationToken cancellationToken)
        => _catalogStore!.SaveMusicAsync(_library!.All, cancellationToken);

    private void RaiseTrackChanged(string trackPath)
        => RxApp.MainThreadScheduler.Schedule(
            () => TrackChanged?.Invoke(this, trackPath));

    private void ReportStatus(string message)
    {
        _onStatus?.Invoke(message);
        RxApp.MainThreadScheduler.Schedule(
            () => StatusChanged?.Invoke(this, message));
    }
}
