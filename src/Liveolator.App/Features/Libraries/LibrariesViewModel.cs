using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Cues;
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
public sealed class LibrariesViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan MinimumVisibleDuration = TimeSpan.FromMinutes(1);

    private readonly MusicLibrary _library;
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly IBeatClock? _beatClock;
    private readonly IMusicCatalogStore? _store;
    private readonly Shared.TrackContextActions? _contextActions;
    private readonly Core.Playlist.DeckTrackLoader? _deckLoader;
    private readonly IAutoCueService? _autoCueService;
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
    // The folders the user has marked as sample sources (the classifier override, B2). Persisted.
    private HashSet<string> _sampleFolders = new(StringComparer.OrdinalIgnoreCase);
    private TrackRowViewModel? _selectedTrack;
    private string _scanStatus = "Add folders, then Scan.";
    private bool _isScanning;
    private bool _isAutoCueing;
    private double _scanProgressValue;
    private string _liveBpm = "—";
    private string _loadStatus = string.Empty;

    // Lifetime control for the fire-and-forget background re-analysis: Dispose() cancels it and waits for
    // it to wind down, so the pass never outlives the view-model. Without this the task leaks past its
    // owner and (under the tests' immediate scheduler) mutates UI collections on a background thread,
    // racing later tests — the root of the App suite's flakiness (doc 27 B0).
    private readonly CancellationTokenSource _lifetime = new();
    private Task _backgroundReanalysis = Task.CompletedTask;

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
        Shared.TrackContextActions? contextActions = null,
        Core.Playlist.DeckTrackLoader? deckLoader = null,
        IAutoCueService? autoCueService = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _dispatcher = dispatcher;
        _beatClock = beatClock;
        _store = store;
        _contextActions = contextActions;
        _autoCueService = autoCueService;
        // The shared load-or-queue policy (doc 09/11): file-reachability check + never cut off a
        // playing deck. A custom loader is injected by tests; the default probes the real filesystem.
        _deckLoader = deckLoader
            ?? (dispatcher is null ? null : new Core.Playlist.DeckTrackLoader(dispatcher, System.IO.File.Exists));
        PlaylistBuilder = playlistBuilder;
        if (_contextActions is not null)
        {
            _contextActions.TrackChanged += OnTrackChanged;
            _contextActions.StatusChanged += OnTrackStatusChanged;
        }

        ScanCommand = ReactiveCommand.CreateFromTask(
            RunScanAsync,
            this.WhenAnyValue(x => x.IsScanning, scanning => !scanning));

        // Auto-cue the whole scanned catalog: place automatic hot cues on every track in the folders.
        // Disabled while a scan or another auto-cue pass is running, and only available when a decoder +
        // cue store were wired (CanAutoCue).
        AutoCueLibraryCommand = ReactiveCommand.CreateFromTask(
            RunAutoCueLibraryAsync,
            this.WhenAnyValue(x => x.IsScanning, x => x.IsAutoCueing,
                (scanning, cueing) => _autoCueService is not null && !scanning && !cueing));

        // Force re-map of the whole catalog ("Rescan"). Shares the IsScanning busy state so it can't
        // overlap a scan or an auto-cue pass (and all three buttons disable while any one runs).
        RescanAllCommand = ReactiveCommand.CreateFromTask(
            RunRescanAllAsync,
            this.WhenAnyValue(x => x.IsScanning, x => x.IsAutoCueing,
                (scanning, cueing) => !scanning && !cueing));

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

    /// <summary>Places automatic hot cues on every track in the scanned folders (persisted to the cue store).</summary>
    public ReactiveCommand<Unit, Unit> AutoCueLibraryCommand { get; }

    /// <summary>
    /// Force re-maps the whole catalog — re-decodes every track for fresh BPM/key/downbeat/cues,
    /// skipping only tracks the user has manually corrected. Disabled while a scan or auto-cue runs.
    /// </summary>
    public ReactiveCommand<Unit, Unit> RescanAllCommand { get; }

    /// <summary>True when automatic hot-cue placement is available (a decoder + cue store were wired); the
    /// UI hides the Auto-cue button otherwise.</summary>
    public bool CanAutoCueLibrary => _autoCueService is not null;

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

    /// <summary>True while a library-wide auto-cue pass is running (drives the Auto-cue button's busy state).</summary>
    public bool IsAutoCueing
    {
        get => _isAutoCueing;
        private set => this.RaiseAndSetIfChanged(ref _isAutoCueing, value);
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
            IReadOnlyList<string> sampleFolders = await _store.LoadSampleFoldersAsync(cancellationToken).ConfigureAwait(false);

            _sampleFolders = new HashSet<string>(sampleFolders, StringComparer.OrdinalIgnoreCase);

            List<TrackRowViewModel>? rows = null;
            if (cached.Count > 0)
            {
                _library.Restore(cached);
                // Re-apply the saved sample designations so a restored catalog comes back with the
                // right Track/Sample split (reclassifies in place, no re-decode).
                if (_sampleFolders.Count > 0)
                    _library.SetSampleFolders(_sampleFolders);
                rows = BuildRows();
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

            // The catalog may hold tracks that were never analyzed (e.g. scanned before a working decoder
            // shipped, so they are Failed with no BPM). Re-analyze them on a background thread so the app
            // comes up immediately and BPM/key fill in progressively (doc 16); the pass persists
            // incrementally, so it resumes on the next run rather than restarting.
            StartBackgroundReanalysis(cancellationToken);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not restore saved library: {ex.Message}");
        }
    }

    // Runs the re-analysis pass off the UI thread (fire-and-forget): the app stays responsive and comes
    // up immediately while previously-unanalyzed tracks get a real BPM/key. A no-op when there is nothing
    // to analyze or no store to persist to.
    private void StartBackgroundReanalysis(CancellationToken cancellationToken)
    {
        if (_store is null || _library.PathsNeedingAnalysis().Count == 0)
            return;

        var service = new CatalogReanalysisService(
            _library, _store,
            onError: e => RxApp.MainThreadScheduler.Schedule(() => ScanStatus = e));

        // Tie the pass to the view-model lifetime (Dispose cancels it) while still honouring any external
        // token the caller supplied.
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        _backgroundReanalysis = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<ReanalysisProgress>(p =>
                    RxApp.MainThreadScheduler.Schedule(() =>
                    {
                        ScanStatus = p.Done >= p.Total
                            ? $"Analysis complete — {p.Analyzed} tracks updated"
                            : $"Analyzing library in background… {p.Done}/{p.Total}";
                        // Surface freshly-analyzed BPM/key periodically without thrashing the UI per track.
                        if (p.Done >= p.Total || p.Done % 25 == 0)
                            RefreshRows();
                    }));

                await service.RunAsync(progress, linked.Token).ConfigureAwait(false);
                RxApp.MainThreadScheduler.Schedule(RefreshRows);
            }
            catch (OperationCanceledException)
            {
                // App shutting down / view-model disposed — nothing to do.
            }
            catch (Exception ex)
            {
                RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Background analysis error: {ex.Message}");
            }
            finally
            {
                linked.Dispose();
            }
        }, linked.Token);
    }

    /// <summary>
    /// Cancels and awaits the background re-analysis so it never outlives the view-model — no leaked work
    /// mutating UI state after disposal (and deterministic teardown for tests, doc 27 B0).
    /// </summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        try
        {
            // The pass checks cancellation between tracks, so it winds down promptly; bound the wait so a
            // stuck pass can never hang disposal.
            _backgroundReanalysis.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // The pass logs its own failures; a cancellation surfacing here is expected, not an error.
        }
        _lifetime.Dispose();
    }

    // Re-projects the current catalog into the visible rows, facets and folder statuses. UI-thread only
    // (mutates ObservableCollections); callers marshal via the main scheduler.
    private void RefreshRows()
    {
        _all = BuildRows();
        RebuildFacets();
        ApplyFilter();
        RefreshFolderStatuses();
    }

    private void OnTrackChanged(object? sender, string trackPath)
    {
        string? selectedPath = SelectedTrack?.Track.File.Path;
        RefreshRows();
        if (selectedPath is not null)
            SelectedTrack = Tracks.FirstOrDefault(
                row => string.Equals(
                    row.Track.File.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
    }

    private void OnTrackStatusChanged(object? sender, string message)
        => ScanStatus = message;

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

    /// <summary>Removes a scan folder (no-op if absent): drops it from the set, clears any sample-folder
    /// designation it carried, prunes the catalogued tracks that lived only under it (exactly what a
    /// re-scan of the reduced set would drop), refreshes the view, and persists the trimmed folder set +
    /// catalog so the removal survives a restart.</summary>
    public void RemoveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Folders.Remove(folder))
            return;

        bool wasSampleFolder = _sampleFolders.Remove(folder);

        _library.PruneToFolders(Folders.ToList());
        // A removed samples folder changes the classifier set, so reclassify the survivors in place.
        if (wasSampleFolder)
            _library.SetSampleFolders(_sampleFolders);

        _all = BuildRows();
        RebuildFacets();
        ApplyFilter();
        RefreshFolderStatuses();

        // Fire-and-forget but fully guarded: a save failure is logged to the status line, never thrown.
        _ = PersistAfterRemoveAsync(wasSampleFolder);
    }

    // Rebuilds the per-folder status rows from the current catalog, seeding each with its sample-folder
    // designation and the toggle + remove callbacks (B2). Must run on the UI scheduler (mutates an
    // ObservableCollection); callers already marshal there.
    private void RefreshFolderStatuses()
    {
        FolderStatuses.Clear();
        foreach (FolderCatalogSummary summary in _library.SummarizeFolders(Folders.ToList()))
            FolderStatuses.Add(new FolderStatusViewModel(
                summary, _sampleFolders.Contains(summary.Folder), OnSampleFolderChanged, RemoveFolder));
    }

    // Projects the current library catalog to row view-models, title-ordered. Shared by scan, restore,
    // and sample-folder reclassification.
    private List<TrackRowViewModel> BuildRows()
        => _library.All
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TrackRowViewModel(t, _contextActions))
            .ToList();

    // The user toggled a folder's "samples" designation in the Folders window (B2). Update the
    // override set, reclassify the catalog in place (no re-decode), refresh the visible rows + facets,
    // and persist — guarded so a save failure surfaces on the status line, never crashes the toggle.
    private void OnSampleFolderChanged(string folder, bool isSample)
    {
        if (isSample)
            _sampleFolders.Add(folder);
        else
            _sampleFolders.Remove(folder);

        _library.SetSampleFolders(_sampleFolders);
        _all = BuildRows();
        RebuildFacets();
        ApplyFilter();
        RefreshFolderStatuses();
        _ = PersistSampleFoldersAsync();
    }

    // Persists just the sample-folder set. Guarded so a save failure is never silent or fatal.
    private async Task PersistSampleFoldersAsync()
    {
        if (_store is null)
            return;

        try
        {
            await _store.SaveSampleFoldersAsync(_sampleFolders.ToList()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not save sample folders: {ex.Message}");
        }
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

            // Re-apply the sample designations so newly-scanned files under a samples folder are
            // classified as Samples (reclassifies the catalog in place; no-op when none are set).
            if (_sampleFolders.Count > 0)
                _library.SetSampleFolders(_sampleFolders);

            List<TrackRowViewModel> rows = BuildRows();

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

    // Force re-maps every catalogued track (re-decode → BPM / key / downbeat / cues), skipping only the
    // tracks the user has manually corrected (their hand-set grid is protected, global #7). Reuses the
    // IsScanning busy state so it can't overlap a scan/auto-cue; progress marshals to the status line;
    // cancellation is tied to the view-model lifetime; a failure surfaces, never crashes (global #16/#26).
    private async Task RunRescanAllAsync()
    {
        if (_library.PathsForFullRemap().Count == 0)
        {
            ScanStatus = "No tracks to re-map — scan folders first.";
            return;
        }

        IsScanning = true;
        ScanProgressValue = 0;
        try
        {
            var service = new CatalogReanalysisService(
                _library, _store, force: true,
                onError: e => RxApp.MainThreadScheduler.Schedule(() => ScanStatus = e));

            var progress = new Progress<ReanalysisProgress>(p =>
                RxApp.MainThreadScheduler.Schedule(() =>
                {
                    ScanStatus = p.Done >= p.Total
                        ? $"Re-map complete — {p.Analyzed} tracks updated"
                        : $"Re-mapping all tracks… {p.Done}/{p.Total}";
                    ScanProgressValue = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
                    // Surface freshly-mapped BPM/key periodically without thrashing the UI per track.
                    if (p.Done >= p.Total || p.Done % 25 == 0)
                        RefreshRows();
                }));

            await service.RunAsync(progress, _lifetime.Token).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                RefreshRows();
                ScanProgressValue = 100;
            });
        }
        catch (OperationCanceledException)
        {
            // The view-model was disposed mid-pass — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Re-map failed: {ex.Message}");
        }
        finally
        {
            RxApp.MainThreadScheduler.Schedule(() => IsScanning = false);
        }
    }

    // Places automatic hot cues on every catalogued track (all files from the scanned folders) and
    // persists them, preserving each track's manual cues. CPU-bound decode/analysis runs off the UI
    // thread (Task.Run); progress marshals back to the status line. Cancellation is tied to the
    // view-model lifetime so the pass never outlives the tab. Guarded — a failure surfaces, never crashes.
    private async Task RunAutoCueLibraryAsync()
    {
        if (_autoCueService is null)
            return;

        List<string> paths = _library.All
            .Select(t => t.File.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            ScanStatus = "No tracks to auto-cue — scan folders first.";
            return;
        }

        IsAutoCueing = true;
        ScanProgressValue = 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            var progress = new Progress<AutoCueProgress>(p =>
                RxApp.MainThreadScheduler.Schedule(() =>
                {
                    ScanStatus = p.Done >= p.Total
                        ? $"Auto-cue complete — {p.Cued} of {p.Total} tracks cued"
                        : $"Placing auto cues… {p.Done}/{p.Total}";
                    ScanProgressValue = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
                }));

            // Decode + structural analysis is CPU-bound; keep it off the UI thread.
            AutoCueOutcome outcome = await Task.Run(
                () => _autoCueService.RunAsync(paths, progress, linked.Token), linked.Token).ConfigureAwait(false);

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                ScanStatus = $"Auto-cue complete — {outcome.Cued} of {outcome.Considered} tracks cued";
                ScanProgressValue = 100;
            });
        }
        catch (OperationCanceledException)
        {
            // App shutting down / tab disposed — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Auto-cue failed: {ex.Message}");
        }
        finally
        {
            RxApp.MainThreadScheduler.Schedule(() => IsAutoCueing = false);
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

    // Persists the trimmed folder set + the pruned catalog after a folder removal (and the sample-folder
    // set when the removed folder had been a samples source). Guarded so a save failure surfaces on the
    // status line but never crashes the removal.
    private async Task PersistAfterRemoveAsync(bool sampleFoldersChanged)
    {
        if (_store is null)
            return;

        try
        {
            await _store.SaveScanFoldersAsync(Folders.ToList()).ConfigureAwait(false);
            await _store.SaveMusicAsync(_library.All).ConfigureAwait(false);
            if (sampleFoldersChanged)
                await _store.SaveSampleFoldersAsync(_sampleFolders.ToList()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not save after removing the folder: {ex.Message}");
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
            Status: SelectedStatus,
            MinDuration: MinimumVisibleDuration);

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
    // Same load-or-queue policy as the deck loads: a playing deck A queues the track instead of cutting
    // it off, and an unreachable file reports why instead of failing deep in the engine (global #26).
    private void PlaySelected()
    {
        if (_dispatcher is null || _deckLoader is null || _selectedTrack is null)
            return;

        Core.Playlist.DeckLoadResult result = _deckLoader.Load(
            slot: 0,
            _selectedTrack.Track.File.Path,
            bpm: _selectedTrack.Track.Bpm?.Bpm ?? 0, // analyzed BPM → deck sync reference (doc 11)
            firstBeatSeconds: _selectedTrack.Track.Bpm?.FirstBeatSeconds ?? 0); // downbeat anchor → phase-match (doc 22 A1)
        if (result.Outcome == Core.Playlist.DeckLoadOutcome.Loaded)
            _dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause));
        LoadStatus = result.Message;
    }

    private void Stop()
        => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.TransportStop));

    // Stage the selected track on a deck slot (A = 0, B = 1) via the action layer — no auto-play
    // (load ≠ play; the performer beat-matches, then brings the deck in). A playing deck queues the
    // track instead; an unreachable file dispatches nothing and reports why (global #26).
    private void LoadToDeck(int slot)
    {
        if (_deckLoader is null || _selectedTrack is null)
            return;

        LoadStatus = _deckLoader.Load(
            slot,
            _selectedTrack.Track.File.Path,
            bpm: _selectedTrack.Track.Bpm?.Bpm ?? 0, // analyzed BPM → deck sync reference (doc 11)
            firstBeatSeconds: _selectedTrack.Track.Bpm?.FirstBeatSeconds ?? 0).Message; // doc 22 A1
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
