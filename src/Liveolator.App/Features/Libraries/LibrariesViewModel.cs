using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using ReactiveUI;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// The Libraries tab. Connects the UI to the real <see cref="MusicLibrary"/> Core module:
/// adds folders, runs the (incremental, background) scan, and exposes the analyzed tracks,
/// search filtering, selection, and Camelot harmonic matches. Holds no Avalonia types.
/// When Live Mode is on it also lets the performer audition the selected track: playback intent
/// goes through the <see cref="IPerformanceActionDispatcher"/> (never a direct engine call — the
/// doc 04 seam), and the live detected tempo is read from the <see cref="IBeatClock"/>.
/// </summary>
public sealed class LibrariesViewModel : ViewModelBase
{
    private readonly MusicLibrary _library;
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IBeatClock? _beatClock;
    private readonly IMusicCatalogStore? _store;
    private readonly Shared.TrackContextActions? _contextActions;
    private List<TrackRowViewModel> _all = new();
    private string? _searchText;
    private string? _selectedArtist;
    private string? _selectedGenre;
    private int? _selectedYear;
    private string? _selectedFileType;
    private MediaAnalysisStatus? _selectedStatus;
    private TrackSortKey _sortKey = TrackSortKey.Title;
    private bool _sortDescending;
    private bool _suppressFilter;
    private TrackRowViewModel? _selectedTrack;
    private string _scanStatus = "Add folders, then Scan.";
    private bool _isScanning;
    private double _scanProgressValue;
    private string _liveBpm = "—";
    private string _loadStatus = string.Empty;

    /// <param name="dispatcher">Action layer for playback intent; null disables Live Mode playback.</param>
    /// <param name="beatClock">Live beat clock to read the detected tempo from; null when Live Mode is off.</param>
    /// <param name="store">Persists the catalog + scan folders across runs; null disables persistence
    /// (the tab still works in-memory for the session).</param>
    public LibrariesViewModel(
        MusicLibrary library,
        IPerformanceActionDispatcher? dispatcher = null,
        IBeatClock? beatClock = null,
        IMusicCatalogStore? store = null,
        Playlists.PlaylistBuilderViewModel? playlistBuilder = null,
        Shared.TrackContextActions? contextActions = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _dispatcher = dispatcher;
        _beatClock = beatClock;
        _store = store;
        _contextActions = contextActions;
        PlaylistBuilder = playlistBuilder;

        ScanCommand = ReactiveCommand.CreateFromTask(
            RunScanAsync,
            this.WhenAnyValue(x => x.IsScanning, scanning => !scanning));

        IObservable<bool> canPlay = this.WhenAnyValue(x => x.SelectedTrack)
            .Select(track => track is not null && _dispatcher is not null);
        PlaySelectedCommand = ReactiveCommand.Create(PlaySelected, canPlay);

        StopCommand = ReactiveCommand.Create(Stop);
        ClearFiltersCommand = ReactiveCommand.Create(ClearFilters);

        // Which deck slots are actually backed is discovered through the dispatcher feedback seam
        // (doc 04) — no engine reference here. A slot reports available iff slot < engine.DeckCount,
        // so "Load → B" stays disabled until a two-deck engine is wired (no silent failure).
        CanLoadToDeckA = DeckSlotAvailable(0);
        CanLoadToDeckB = DeckSlotAvailable(1);
        LoadToDeckACommand = ReactiveCommand.Create(
            () => LoadToDeck(0),
            this.WhenAnyValue(x => x.SelectedTrack).Select(t => t is not null && CanLoadToDeckA));
        LoadToDeckBCommand = ReactiveCommand.Create(
            () => LoadToDeck(1),
            this.WhenAnyValue(x => x.SelectedTrack).Select(t => t is not null && CanLoadToDeckB));

        if (_beatClock is not null)
        {
            UpdateLiveBpm(_beatClock.Current);
            _beatClock.StateChanged += OnBeatStateChanged;
        }

        // Any filter or sort change re-runs the query (the search box, the facet pickers, the
        // status filter, and the sort key/direction all funnel through one ApplyFilter). Merged as
        // unit signals because WhenAnyValue caps at a few typed selectors.
        Observable.Merge(
                this.WhenAnyValue(x => x.SearchText).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedArtist).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedGenre).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedYear).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedFileType).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedStatus).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SortKey).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SortDescending).Select(_ => Unit.Default))
            .Subscribe(_ => ApplyFilter());
        this.WhenAnyValue(x => x.SelectedTrack).Subscribe(_ => RebuildMatches());
    }

    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = new();
    public ObservableCollection<TrackRowViewModel> HarmonicMatches { get; } = new();

    /// <summary>Distinct facet values from the catalog (B1). A null selection on each = "all".</summary>
    public ObservableCollection<string> Artists { get; } = new();
    public ObservableCollection<string> Genres { get; } = new();
    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<string> FileTypes { get; } = new();

    /// <summary>The status-filter choices (null = "Any"); fixed, so it is built once.</summary>
    public IReadOnlyList<MediaAnalysisStatus?> StatusOptions { get; } = new MediaAnalysisStatus?[]
    {
        null, MediaAnalysisStatus.Ok, MediaAnalysisStatus.PartiallyAnalyzed, MediaAnalysisStatus.Failed,
    };

    /// <summary>The sortable columns offered in the sort picker.</summary>
    public IReadOnlyList<TrackSortKey> SortKeys { get; } = new[]
    {
        TrackSortKey.Title, TrackSortKey.Bpm, TrackSortKey.Key, TrackSortKey.Duration,
    };

    /// <summary>Per-folder scan/update status (one row per added folder) for the folder-status window.</summary>
    public ObservableCollection<FolderStatusViewModel> FolderStatuses { get; } = new();

    /// <summary>The playlist/set builder opened from the "Playlists" button; null disables the button.</summary>
    public Playlists.PlaylistBuilderViewModel? PlaylistBuilder { get; }

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }
    public ReactiveCommand<Unit, Unit> PlaySelectedCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadToDeckACommand { get; }
    public ReactiveCommand<Unit, Unit> LoadToDeckBCommand { get; }

    /// <summary>Resets every facet, the status filter, and the search box back to "show all" (B1).</summary>
    public ReactiveCommand<Unit, Unit> ClearFiltersCommand { get; }

    /// <summary>True when playback is wired (Live Mode on); the UI hides transport controls otherwise.</summary>
    public bool IsLiveModeEnabled => _dispatcher is not null;

    /// <summary>True when deck slot A / B is backed by the engine (drives the Load buttons).</summary>
    public bool CanLoadToDeckA { get; }
    public bool CanLoadToDeckB { get; }

    /// <summary>Confirmation of the last "Load → Deck" action (never a silent success).</summary>
    public string LoadStatus
    {
        get => _loadStatus;
        private set => this.RaiseAndSetIfChanged(ref _loadStatus, value);
    }

    /// <summary>The live detected tempo of the playing track, or "—" before a lock.</summary>
    public string LiveBpm
    {
        get => _liveBpm;
        private set => this.RaiseAndSetIfChanged(ref _liveBpm, value);
    }

    public string? SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    /// <summary>Selected artist facet (null = all artists).</summary>
    public string? SelectedArtist
    {
        get => _selectedArtist;
        set => this.RaiseAndSetIfChanged(ref _selectedArtist, value);
    }

    /// <summary>Selected genre facet (null = all genres).</summary>
    public string? SelectedGenre
    {
        get => _selectedGenre;
        set => this.RaiseAndSetIfChanged(ref _selectedGenre, value);
    }

    /// <summary>Selected year facet (null = all years).</summary>
    public int? SelectedYear
    {
        get => _selectedYear;
        set => this.RaiseAndSetIfChanged(ref _selectedYear, value);
    }

    /// <summary>Selected file-type facet (null = all file types).</summary>
    public string? SelectedFileType
    {
        get => _selectedFileType;
        set => this.RaiseAndSetIfChanged(ref _selectedFileType, value);
    }

    /// <summary>Selected analysis-status filter (null = any status).</summary>
    public MediaAnalysisStatus? SelectedStatus
    {
        get => _selectedStatus;
        set => this.RaiseAndSetIfChanged(ref _selectedStatus, value);
    }

    /// <summary>The column the track list is ordered by.</summary>
    public TrackSortKey SortKey
    {
        get => _sortKey;
        set => this.RaiseAndSetIfChanged(ref _sortKey, value);
    }

    /// <summary>Descending sort when true (the toggle next to the sort picker).</summary>
    public bool SortDescending
    {
        get => _sortDescending;
        set => this.RaiseAndSetIfChanged(ref _sortDescending, value);
    }

    public TrackRowViewModel? SelectedTrack
    {
        get => _selectedTrack;
        set => this.RaiseAndSetIfChanged(ref _selectedTrack, value);
    }

    public string ScanStatus
    {
        get => _scanStatus;
        private set => this.RaiseAndSetIfChanged(ref _scanStatus, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => this.RaiseAndSetIfChanged(ref _isScanning, value);
    }

    /// <summary>Overall scan progress (0–100) for the folder-status window's progress bar.</summary>
    public double ScanProgressValue
    {
        get => _scanProgressValue;
        private set => this.RaiseAndSetIfChanged(ref _scanProgressValue, value);
    }

    /// <summary>
    /// Restores the previously-persisted state (scan folders + analyzed catalog) so the app opens
    /// where the last run left off. Called once at startup. A persistence failure degrades to an
    /// empty session with a surfaced status — it never blocks the app (global standards #16/#26).
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_store is null)
            return;

        try
        {
            IReadOnlyList<string> folders = await _store.LoadScanFoldersAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<MusicTrack> cached = await _store.LoadMusicAsync(cancellationToken).ConfigureAwait(false);

            List<TrackRowViewModel>? rows = null;
            if (cached.Count > 0)
            {
                _library.Restore(cached);
                rows = _library.All
                    .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(t => new TrackRowViewModel(t, _contextActions))
                    .ToList();
            }

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                foreach (string folder in folders)
                    if (!Folders.Contains(folder))
                        Folders.Add(folder);

                if (rows is not null)
                {
                    _all = rows;
                    RebuildFacets();
                    ApplyFilter();
                    ScanStatus = $"{rows.Count} tracks (restored)";
                }

                RefreshFolderStatuses();
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not restore saved library: {ex.Message}");
        }
    }

    /// <summary>Adds a folder root to scan (no-op if blank or already present), persisting the
    /// updated set so it survives a restart even before the next scan.</summary>
    public void AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || Folders.Contains(folder))
            return;

        Folders.Add(folder);
        RefreshFolderStatuses(); // show the new folder immediately (0 tracks until the next scan)
        // Fire-and-forget but fully guarded: a save failure is logged to the status line, never thrown.
        _ = PersistFoldersAsync();
    }

    // Rebuilds the per-folder status rows from the current catalog. Must run on the UI scheduler
    // (mutates an ObservableCollection); callers already marshal there.
    private void RefreshFolderStatuses()
    {
        FolderStatuses.Clear();
        foreach (FolderCatalogSummary summary in _library.SummarizeFolders(Folders.ToList()))
            FolderStatuses.Add(new FolderStatusViewModel(summary));
    }

    private async Task RunScanAsync()
    {
        IsScanning = true;
        ScanProgressValue = 0;
        // Snapshot the folder set on the calling thread so the persisted copy matches what was scanned
        // and we never read the UI-owned ObservableCollection off-thread.
        List<string> folders = Folders.ToList();
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatus = p.Total == 0 ? "No new files." : $"Analyzing {p.Done} / {p.Total}…";
                ScanProgressValue = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
            });

            await _library.ScanAsync(folders, progress).ConfigureAwait(false);

            List<TrackRowViewModel> rows = _library.All
                .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .Select(t => new TrackRowViewModel(t, _contextActions))
                .ToList();

            // Persist the fresh catalog + the folders that produced it, so the next run restores them.
            await PersistCatalogAsync(folders).ConfigureAwait(false);

            // Collection mutations must happen on the UI scheduler (immediate in tests).
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                _all = rows;
                RebuildFacets();
                ApplyFilter();
                ScanStatus = $"{rows.Count} tracks";
                ScanProgressValue = 100;
                RefreshFolderStatuses();
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Scan failed: {ex.Message}");
        }
        finally
        {
            RxApp.MainThreadScheduler.Schedule(() => IsScanning = false);
        }
    }

    // Saves the analyzed catalog and the scan folders. Guarded: a persistence failure surfaces on the
    // status line but never aborts a completed scan (the in-memory results are still shown).
    private async Task PersistCatalogAsync(IReadOnlyList<string> folders)
    {
        if (_store is null)
            return;

        try
        {
            await _store.SaveMusicAsync(_library.All).ConfigureAwait(false);
            await _store.SaveScanFoldersAsync(folders).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Scan done; saving the catalog failed: {ex.Message}");
        }
    }

    // Persists just the folder set (after an add). Guarded so a save failure is never silent or fatal.
    private async Task PersistFoldersAsync()
    {
        if (_store is null)
            return;

        try
        {
            await _store.SaveScanFoldersAsync(Folders.ToList()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not save folders: {ex.Message}");
        }
    }

    // Re-runs the composed facet/status/text filter (TrackQuery) then the sort (TrackSort) over the
    // catalog, both pure Core logic, and projects the surviving tracks back to their row view-models.
    // Suppressed during a multi-property reset so ClearFilters re-queries exactly once.
    private void ApplyFilter()
    {
        if (_suppressFilter)
            return;

        var rowByTrack = _all.ToDictionary(r => r.Track);
        var filter = new TrackFilter(
            Text: SearchText,
            Artist: SelectedArtist,
            Genre: SelectedGenre,
            Year: SelectedYear,
            FileType: SelectedFileType,
            Status: SelectedStatus);

        IReadOnlyList<MusicTrack> filtered = TrackQuery.Apply(rowByTrack.Keys, filter, TrackQuery.MaxResults);
        IReadOnlyList<MusicTrack> ordered = TrackSort.Apply(filtered, SortKey, SortDescending);

        Tracks.Clear();
        foreach (MusicTrack track in ordered)
            Tracks.Add(rowByTrack[track]);
    }

    // Recomputes the facet dropdowns from the current catalog (after a scan or restore) and drops any
    // selection that no longer exists, so the pickers never offer a stale value. Must run on the UI
    // scheduler (mutates ObservableCollections); callers already marshal there.
    private void RebuildFacets()
    {
        TrackFacets facets = TrackFacets.Of(_all.Select(r => r.Track));
        Replace(Artists, facets.Artists);
        Replace(Genres, facets.Genres);
        Replace(Years, facets.Years);
        Replace(FileTypes, facets.FileTypes);

        if (SelectedArtist is not null && !facets.Artists.Contains(SelectedArtist)) SelectedArtist = null;
        if (SelectedGenre is not null && !facets.Genres.Contains(SelectedGenre)) SelectedGenre = null;
        if (SelectedYear is { } y && !facets.Years.Contains(y)) SelectedYear = null;
        if (SelectedFileType is not null && !facets.FileTypes.Contains(SelectedFileType)) SelectedFileType = null;
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }

    // Resets every filter back to "show all" in one batch, re-querying just once at the end.
    private void ClearFilters()
    {
        _suppressFilter = true;
        try
        {
            SearchText = null;
            SelectedArtist = null;
            SelectedGenre = null;
            SelectedYear = null;
            SelectedFileType = null;
            SelectedStatus = null;
        }
        finally
        {
            _suppressFilter = false;
        }

        ApplyFilter();
    }

    private void RebuildMatches()
    {
        HarmonicMatches.Clear();
        if (_selectedTrack is null)
            return;

        foreach (MusicTrack match in _library.HarmonicMatches(_selectedTrack.Track)
                     .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
            HarmonicMatches.Add(new TrackRowViewModel(match, _contextActions));
    }

    // Load + play the selected track via the action layer (doc 04) — the UI never touches the engine.
    private void PlaySelected()
    {
        if (_dispatcher is null || _selectedTrack is null)
            return;

        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack,
            Value: _selectedTrack.Track.Bpm?.Bpm ?? 0, // analyzed BPM → deck sync reference (doc 11)
            Argument: _selectedTrack.Track.File.Path));
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetFirstBeat,
            Value: _selectedTrack.Track.Bpm?.FirstBeatSeconds ?? 0)); // downbeat anchor → phase-match (doc 22 A1)
        _dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause));
    }

    private void Stop()
        => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.TransportStop));

    // Stage the selected track on a deck slot (A = 0, B = 1) via the action layer — no auto-play
    // (load ≠ play; the performer beat-matches, then brings the deck in).
    private void LoadToDeck(int slot)
    {
        if (_dispatcher is null || _selectedTrack is null)
            return;

        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, Slot: slot,
            Value: _selectedTrack.Track.Bpm?.Bpm ?? 0, // analyzed BPM → deck sync reference (doc 11)
            Argument: _selectedTrack.Track.File.Path));
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetFirstBeat, Slot: slot,
            Value: _selectedTrack.Track.Bpm?.FirstBeatSeconds ?? 0)); // downbeat anchor → phase-match (doc 22 A1)
        LoadStatus = $"Loaded \"{_selectedTrack.Title}\" → Deck {(slot == 0 ? "A" : "B")}";
    }

    // A deck slot is loadable only if the engine backs it — discovered via the feedback seam
    // (DeckPlayPause reports IsAvailable iff slot < engine.DeckCount). Null dispatcher ⇒ no decks.
    private bool DeckSlotAvailable(int slot)
        => _dispatcher?.GetFeedback(PerformanceActionKind.DeckPlayPause, slot).IsAvailable ?? false;

    private void OnBeatStateChanged(object? sender, BeatClockState state)
        => RxApp.MainThreadScheduler.Schedule(() => UpdateLiveBpm(state));

    private void UpdateLiveBpm(BeatClockState state)
        => LiveBpm = state.Bpm > 0 ? $"{state.Bpm:0.0} BPM" : "—";
}
