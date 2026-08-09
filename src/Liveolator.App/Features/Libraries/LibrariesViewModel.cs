using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Beat;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Doctor;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
using Liveolator.Core.Waveform;
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
    private readonly LibraryDoctor? _doctor;
    private readonly IMediaIdentityStore? _identityStore;
    private readonly VisualMediaLibrary? _visualLibrary;
    private readonly IFileContentHasher? _contentHasher;
    private readonly Shared.TrackContextActions? _contextActions;
    private readonly Core.Playlist.DeckTrackLoader? _deckLoader;
    private readonly IAutoCueService? _autoCueService;
    private readonly IHotCueStore? _hotCueStore;
    private readonly IWaveformProvider? _waveformProvider;
    private readonly Core.Enrichment.IMetadataProvider? _metadataProvider;
    // Reachability probe used to skip unreachable / online-only cloud placeholders before a decode so a
    // single un-downloaded OneDrive file can't hang the auto-cue pass. Injected so tests stay pure.
    private readonly Func<string, bool> _isLocallyDecodable;
    // Track paths that have at least one stored hot cue, read once per row rebuild (one batch store read,
    // not an N-load storm) to light each row's CUE badge.
    private HashSet<string> _pathsWithCues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Core.Library.Import.LibraryImportService? _importService;
    private readonly IReadOnlyList<Core.Library.Import.ILibraryImporter> _importers;
    private readonly IReadOnlyList<Core.Library.Import.IFolderLibraryImporter> _folderImporters;
    // Drops a stale hot-cue load when the selection moves on before an async store read returns.
    private int _hotCueLoadSequence;
    // Same stale-load guard for the (separate, slower) waveform decode. Kept apart from the cue sequence so a
    // cue edit (which re-reads cues, not the wave) never drops an in-flight waveform load.
    private int _waveformLoadSequence;
    // Library overview waveform detail — fewer buckets than the deck (6k) since this strip is display-only.
    private const int WaveformBuckets = 2_000;
    // The selected track's decoded overview + stored cue record, held so the hot-cue markers can be
    // recomputed from whichever of the two async loads arrives last (both are needed to map cues → 0..1).
    private WaveformOverview? _selectedOverview;
    private TrackCueRecord? _selectedCueRecord;
    private List<TrackRowViewModel> _all = new();
    private string? _searchText;
    private string? _bpmMinText;
    private string? _bpmMaxText;
    private string? _selectedArtist;
    private string? _selectedGenre;
    private int? _selectedYear;
    private string? _selectedFileType;
    private MediaAnalysisStatus? _selectedStatus;
    private bool _showShortClips = true;
    private int _shortClipCount;
    private TrackSortKey _sortKey = TrackSortKey.Title;
    private bool _sortDescending;
    private bool _suppressFilter;
    // The folders the user has marked as sample sources (the classifier override, B2). Persisted.
    private HashSet<string> _sampleFolders = new(StringComparer.OrdinalIgnoreCase);
    private TrackRowViewModel? _selectedTrack;
    private LibraryIssueViewModel? _selectedLibraryIssue;
    private string _scanStatus = "Add folders, then Scan.";
    private string _doctorSummary = "Run Scan health to inspect the library.";
    private bool _isScanning;
    private bool _isAutoCueing;
    // Set once when a per-track incremental-scan save first fails, so the error is surfaced a single time
    // (not once per track) and the scan is never aborted (global #16/#26).
    private bool _scanPersistFailed;
    private double _scanProgressValue;
    private string _liveBpm = "—";
    private string _loadStatus = string.Empty;
    private string _resultSummary = string.Empty;

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
        LibraryDoctor? doctor = null,
        IMediaIdentityStore? identityStore = null,
        VisualMediaLibrary? visualLibrary = null,
        IFileContentHasher? contentHasher = null,
        Playlists.PlaylistBuilderViewModel? playlistBuilder = null,
        Shared.TrackContextActions? contextActions = null,
        Core.Playlist.DeckTrackLoader? deckLoader = null,
        IAutoCueService? autoCueService = null,
        IHotCueStore? hotCueStore = null,
        IWaveformProvider? waveformProvider = null,
        Core.Library.Import.LibraryImportService? importService = null,
        IReadOnlyList<Core.Library.Import.ILibraryImporter>? importers = null,
        IReadOnlyList<Core.Library.Import.IFolderLibraryImporter>? folderImporters = null,
        Func<string, bool>? isLocallyDecodable = null,
        Core.Enrichment.IMetadataProvider? metadataProvider = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _dispatcher = dispatcher;
        _beatClock = beatClock;
        _store = store;
        _doctor = doctor;
        _identityStore = identityStore;
        _visualLibrary = visualLibrary;
        _contentHasher = contentHasher;
        _contextActions = contextActions;
        _autoCueService = autoCueService;
        _hotCueStore = hotCueStore;
        _waveformProvider = waveformProvider;
        _metadataProvider = metadataProvider;
        _importService = importService;
        _importers = importers ?? Array.Empty<Core.Library.Import.ILibraryImporter>();
        _folderImporters = folderImporters ?? Array.Empty<Core.Library.Import.IFolderLibraryImporter>();
        _isLocallyDecodable = isLocallyDecodable ?? Core.Library.Music.TrackFileReachability.IsLocallyDecodable;
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

        // The single LIBRARIES "Scan" button (owner request, 2026-06-30): one click runs the whole
        // library pass — scan → rescan → auto-cue. Blocked while any step is running so it can't
        // overlap itself or the building-block commands above.
        ScanAllCommand = ReactiveCommand.CreateFromTask(
            RunScanAllAsync,
            this.WhenAnyValue(x => x.IsScanning, x => x.IsAutoCueing,
                (scanning, cueing) => !scanning && !cueing));

        ScanHealthCommand = ReactiveCommand.CreateFromTask(
            RunScanHealthAsync,
            this.WhenAnyValue(x => x.IsScanning, x => x.IsAutoCueing,
                (scanning, cueing) => _doctor is not null && !scanning && !cueing));
        RemoveIssueFromCatalogCommand = ReactiveCommand.CreateFromTask(
            RemoveSelectedIssueFromCatalogAsync,
            this.WhenAnyValue(x => x.SelectedLibraryIssue)
                .Select(i => i?.Kind == LibraryIssueKind.MissingFile));
        ReanalyzeIssueCommand = ReactiveCommand.CreateFromTask(
            ReanalyzeSelectedIssueAsync,
            this.WhenAnyValue(x => x.SelectedLibraryIssue)
                .Select(i => i?.Kind is LibraryIssueKind.BrokenAnalysis or LibraryIssueKind.UnanalyzedTrack or LibraryIssueKind.LowConfidenceAnalysis));

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
                this.WhenAnyValue(x => x.BpmMinText).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.BpmMaxText).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedArtist).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedGenre).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedYear).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedFileType).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedStatus).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.ShowShortClips).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SortKey).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SortDescending).Select(_ => Unit.Default))
            .Subscribe(_ => ApplyFilter());
        // Auto-cue just the selected track (the library detail "Auto Hot-Cue" button): place automatic hot
        // cues on it, then refresh the list + the waveform markers. Same guard as the library-wide pass.
        AutoCueSelectedCommand = ReactiveCommand.CreateFromTask(
            RunAutoCueSelectedAsync,
            this.WhenAnyValue(x => x.SelectedTrack, x => x.IsScanning, x => x.IsAutoCueing,
                (track, scanning, cueing) => track is not null && _autoCueService is not null && !scanning && !cueing));

        // Click the waveform to drop a manual hot cue there (the strip's SeekCommand carries the 0..1
        // position). Only when a track is selected and a cue store is wired to persist it.
        MarkCueCommand = ReactiveCommand.CreateFromTask<double>(
            MarkCueAtFractionAsync,
            this.WhenAnyValue(x => x.SelectedTrack).Select(t => t is not null && _hotCueStore is not null));

        SetRatingCommand = ReactiveCommand.CreateFromTask<int>(
            SetSelectedRatingAsync,
            this.WhenAnyValue(x => x.SelectedTrack).Select(t => t is not null));

        this.WhenAnyValue(x => x.SelectedTrack).Subscribe(_ =>
        {
            RebuildMatches();
            RebuildHotCues();
            RebuildWaveform();
        });
    }

    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = new();
    public ObservableCollection<TrackRowViewModel> HarmonicMatches { get; } = new();

    /// <summary>The stored hot cue points of the selected track, ordered by pad number (B/doc 11).</summary>
    public ObservableCollection<HotCueDisplayViewModel> HotCues { get; } = new();

    /// <summary>True when the selected track has at least one stored hot cue (drives the section's empty state).</summary>
    public bool HasHotCues => HotCues.Count > 0;

    private IReadOnlyList<float>? _waveform;
    private IReadOnlyList<float>? _kickPeaks;
    private IReadOnlyList<float>? _midPeaks;
    private IReadOnlyList<float>? _highPeaks;
    private IReadOnlyList<double>? _hotCueMarkers;
    private string _waveformStatus = string.Empty;

    /// <summary>Broadband overview peaks of the selected track for the bottom waveform strip; null when
    /// none is loaded (the strip then shows its "no track" placeholder).</summary>
    public IReadOnlyList<float>? Waveform { get => _waveform; private set => this.RaiseAndSetIfChanged(ref _waveform, value); }
    public IReadOnlyList<float>? KickPeaks { get => _kickPeaks; private set => this.RaiseAndSetIfChanged(ref _kickPeaks, value); }
    public IReadOnlyList<float>? MidPeaks { get => _midPeaks; private set => this.RaiseAndSetIfChanged(ref _midPeaks, value); }
    public IReadOnlyList<float>? HighPeaks { get => _highPeaks; private set => this.RaiseAndSetIfChanged(ref _highPeaks, value); }

    /// <summary>The selected track's hot-cue positions as 0..1 track fractions, overlaid on the waveform
    /// strip; null/empty draws no markers.</summary>
    public IReadOnlyList<double>? HotCueMarkers { get => _hotCueMarkers; private set => this.RaiseAndSetIfChanged(ref _hotCueMarkers, value); }

    /// <summary>Why there's no waveform to show (loading / offline / undecodable), or empty when the strip
    /// is drawn — so an un-decodable track explains itself instead of showing a blank strip (global #26).</summary>
    public string WaveformStatus { get => _waveformStatus; private set => this.RaiseAndSetIfChanged(ref _waveformStatus, value); }

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
        TrackSortKey.Rating, TrackSortKey.DateAdded, TrackSortKey.PlayCount,
    };

    /// <summary>Per-folder scan/update status (one row per added folder) for the folder-status window.</summary>
    public ObservableCollection<FolderStatusViewModel> FolderStatuses { get; } = new();

    /// <summary>Library Doctor findings from the latest non-mutating health scan.</summary>
    public ObservableCollection<LibraryIssueViewModel> LibraryIssues { get; } = new();

    /// <summary>The playlist/set builder opened from the "Playlists" button; null disables the button.</summary>
    public Playlists.PlaylistBuilderViewModel? PlaylistBuilder { get; }

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }

    /// <summary>
    /// The one-button LIBRARIES "Scan" action (owner request, 2026-06-30): scans the folders, force
    /// re-maps every track (BPM/key/beat grid/cues), then places automatic hot cues — in sequence.
    /// Composes <see cref="ScanCommand"/>, <see cref="RescanAllCommand"/> and
    /// <see cref="AutoCueLibraryCommand"/>; the auto-cue step is skipped when no service was wired.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ScanAllCommand { get; }

    /// <summary>Places automatic hot cues on every track in the scanned folders (persisted to the cue store).</summary>
    public ReactiveCommand<Unit, Unit> AutoCueLibraryCommand { get; }

    /// <summary>Places automatic hot cues on the selected track only (the library detail "Auto Hot-Cue" button).</summary>
    public ReactiveCommand<Unit, Unit> AutoCueSelectedCommand { get; }

    /// <summary>Adds a manual hot cue at a clicked 0..1 waveform position (the strip's click seam), placing
    /// it in the next free slot and persisting it. The parameter is the clicked track fraction.</summary>
    public ReactiveCommand<double, Unit> MarkCueCommand { get; }

    /// <summary>Sets the selected track's 0–5 star rating (the parameter is the star count) and persists it.</summary>
    public ReactiveCommand<int, Unit> SetRatingCommand { get; }

    /// <summary>
    /// Force re-maps the whole catalog — re-decodes every track for fresh BPM/key/downbeat/cues,
    /// skipping only tracks the user has manually corrected. Disabled while a scan or auto-cue runs.
    /// </summary>
    public ReactiveCommand<Unit, Unit> RescanAllCommand { get; }

    public ReactiveCommand<Unit, Unit> ScanHealthCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveIssueFromCatalogCommand { get; }
    public ReactiveCommand<Unit, Unit> ReanalyzeIssueCommand { get; }

    public bool CanUseLibraryDoctor => _doctor is not null;

    /// <summary>True when automatic hot-cue placement is available (a decoder + cue store were wired); the
    /// UI hides the Auto-cue button otherwise.</summary>
    public bool CanAutoCueLibrary => _autoCueService is not null;

    /// <summary>True when importing another DJ app's library is available (a service + parser were wired).</summary>
    public bool CanImportLibrary =>
        _importService is not null && (_importers.Count > 0 || _folderImporters.Count > 0);

    /// <summary>The single-file source formats that can be imported (e.g. "Rekordbox", "Traktor").</summary>
    public IReadOnlyList<string> ImportFormatNames => _importers.Select(i => i.FormatName).ToList();

    /// <summary>The folder-based source formats that can be imported (e.g. "Serato").</summary>
    public IReadOnlyList<string> FolderImportFormatNames => _folderImporters.Select(i => i.FormatName).ToList();

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

    /// <summary>Lower BPM bound for the filter (blank = no lower bound). Free text so a DJ can type "124".</summary>
    public string? BpmMinText
    {
        get => _bpmMinText;
        set => this.RaiseAndSetIfChanged(ref _bpmMinText, value);
    }

    /// <summary>Upper BPM bound for the filter (blank = no upper bound).</summary>
    public string? BpmMaxText
    {
        get => _bpmMaxText;
        set => this.RaiseAndSetIfChanged(ref _bpmMaxText, value);
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

    /// <summary>
    /// Shows tracks under one minute (edits, stings, acapellas, loops). Default true — a hard floor used
    /// to hide them with no affordance, so DJs couldn't find their short material (doc 31 H2). Unchecking
    /// hides them; <see cref="ShortClipCount"/> reports how many so the exclusion is never silent.
    /// </summary>
    public bool ShowShortClips
    {
        get => _showShortClips;
        set => this.RaiseAndSetIfChanged(ref _showShortClips, value);
    }

    /// <summary>How many catalogued tracks are under one minute — surfaced next to the toggle.</summary>
    public int ShortClipCount
    {
        get => _shortClipCount;
        private set => this.RaiseAndSetIfChanged(ref _shortClipCount, value);
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

    public LibraryIssueViewModel? SelectedLibraryIssue
    {
        get => _selectedLibraryIssue;
        set => this.RaiseAndSetIfChanged(ref _selectedLibraryIssue, value);
    }

    public string ScanStatus
    {
        get => _scanStatus;
        private set => this.RaiseAndSetIfChanged(ref _scanStatus, value);
    }

    /// <summary>How many tracks the current filter shows out of the whole catalog (e.g. "137 of 4502"),
    /// flagging when the result set is capped — so a large library never silently drops rows.</summary>
    public string ResultSummary
    {
        get => _resultSummary;
        private set => this.RaiseAndSetIfChanged(ref _resultSummary, value);
    }

    public string DoctorSummary
    {
        get => _doctorSummary;
        private set => this.RaiseAndSetIfChanged(ref _doctorSummary, value);
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
                _ = RefreshCuePresenceAsync();
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
                        // Surface freshly-analyzed BPM/key as they come in — eager for the first dozen,
                        // then every 25 (ShouldRevealDuringScan); the final tick always refreshes.
                        if (p.Done >= p.Total || ShouldRevealDuringScan(p.Done))
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

    // Decides whether a scan/re-analysis progress tick should re-project the visible list. Refresh
    // eagerly for the first dozen processed tracks — so the list starts filling from the very first
    // file instead of showing nothing until 25 are done (the reported "scanned tracks don't appear
    // until the scan finishes") — then throttle to every 25 so a large catalog isn't rebuilt per file
    // (O(n^2) thrash). Pure so it unit-tests without a scan.
    internal static bool ShouldRevealDuringScan(int done)
        => done > 0 && (done <= 12 || done % 25 == 0);

    // Re-projects the current catalog into the visible rows, facets and folder statuses. UI-thread only
    // (mutates ObservableCollections); callers marshal via the main scheduler.
    private void RefreshRows()
    {
        _all = BuildRows();
        RebuildFacets();
        ApplyFilter();
        RefreshFolderStatuses();
        // Re-read cue presence (one batch store call) so the row CUE badges reflect the latest cues.
        _ = RefreshCuePresenceAsync();
    }

    private async Task RunScanHealthAsync()
    {
        if (_doctor is null)
            return;

        try
        {
            // The hash/identity/scan pipeline itself is pure orchestration over seams and lives in Core
            // (LibraryHealthScanner) — this view-model only supplies the catalog and renders the report.
            LibraryDoctorReport report = await new LibraryHealthScanner(
                    _doctor, _identityStore, _contentHasher)
                .ScanAsync(
                    _library.All,
                    _visualLibrary?.All ?? Array.Empty<VisualAsset>(),
                    Folders.ToList(),
                    _lifetime.Token)
                .ConfigureAwait(false);

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                Replace(LibraryIssues, report.Issues.Select(i => new LibraryIssueViewModel(i)).ToList());
                SelectedLibraryIssue = LibraryIssues.FirstOrDefault();
                DoctorSummary =
                    $"{report.Issues.Count} issues | {report.MissingCount} missing | " +
                    $"{report.DuplicateCount} duplicate groups | {report.BrokenCount} analysis issues";
                ScanStatus = $"Library Doctor: {DoctorSummary}";
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                DoctorSummary = $"Health scan failed: {ex.Message}";
                ScanStatus = DoctorSummary;
            });
        }
    }

    private async Task RemoveSelectedIssueFromCatalogAsync()
    {
        if (SelectedLibraryIssue?.Issue is not { Kind: LibraryIssueKind.MissingFile } issue)
            return;

        if (!_library.Remove(issue.Path))
            return;

        RefreshRows();
        LibraryIssues.Remove(SelectedLibraryIssue);
        SelectedLibraryIssue = LibraryIssues.FirstOrDefault();
        ScanStatus = $"Removed missing track from catalog: {issue.Title}";

        if (_store is not null)
        {
            try
            {
                // Delete just this one path — the per-row store is upsert-only, so re-saving the catalog
                // would leave the removed track's row behind.
                await _store.DeleteTrackAsync(issue.Path, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Removed from memory; save failed: {ex.Message}");
            }
        }
    }

    private async Task ReanalyzeSelectedIssueAsync()
    {
        if (SelectedLibraryIssue?.Issue is not { } issue)
            return;
        if (issue.Kind is not (LibraryIssueKind.BrokenAnalysis or LibraryIssueKind.UnanalyzedTrack or LibraryIssueKind.LowConfidenceAnalysis))
            return;

        bool ok = await _library.ForceReanalyzeAsync(issue.Path, _lifetime.Token).ConfigureAwait(false);
        if (_store is not null)
            await _store.SaveMusicAsync(_library.All, _lifetime.Token).ConfigureAwait(false);

        RxApp.MainThreadScheduler.Schedule(() =>
        {
            RefreshRows();
            ScanStatus = ok ? $"Re-analyzed \"{issue.Title}\"." : $"Re-analysis failed for \"{issue.Title}\".";
        });
        await RunScanHealthAsync().ConfigureAwait(false);
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

    /// <summary>Surfaces a picked folder the OS won't resolve to a local path (a virtual location, or
    /// a network share that isn't mounted) instead of dropping it silently (global #26).</summary>
    public void ReportFolderUnavailable(string name)
        => ScanStatus = $"Couldn't add \"{name}\" — not a reachable local/network path. Map it to a drive letter, then add that.";

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

        // Capture which tracks the prune drops so they can be deleted from the per-row store: SaveMusic
        // is upsert-only (doc 31 M1), so re-saving the survivors would NOT remove the pruned rows.
        var before = _library.All.Select(t => t.File.Path).ToList();
        _library.PruneToFolders(Folders.ToList());
        var surviving = new HashSet<string>(
            _library.All.Select(t => t.File.Path), StringComparer.OrdinalIgnoreCase);
        List<string> prunedPaths = before.Where(p => !surviving.Contains(p)).ToList();

        // A removed samples folder changes the classifier set, so reclassify the survivors in place.
        if (wasSampleFolder)
            _library.SetSampleFolders(_sampleFolders);

        _all = BuildRows();
        RebuildFacets();
        ApplyFilter();
        RefreshFolderStatuses();

        // Fire-and-forget but fully guarded: a save failure is logged to the status line, never thrown.
        _ = PersistAfterRemoveAsync(wasSampleFolder, prunedPaths);
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
            .Select(t => new TrackRowViewModel(
                t, _contextActions, hasCues: _pathsWithCues.Contains(t.File.Path)))
            .ToList();

    // Reads the set of track paths that have stored hot cues in ONE batch store call (not a per-row load
    // storm), caches it, and re-projects the rows so the CUE badge lights up. Fire-and-forget from row
    // rebuilds; guarded so a store failure surfaces on the status line but never crashes the tab.
    private async Task RefreshCuePresenceAsync()
    {
        if (_hotCueStore is null)
            return;

        try
        {
            IReadOnlyCollection<string> paths =
                await _hotCueStore.ListPathsWithCuesAsync(_lifetime.Token).ConfigureAwait(false);
            var updated = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                _pathsWithCues = updated;
                _all = BuildRows();
                ApplyFilter();
            });
        }
        catch (OperationCanceledException)
        {
            // The view-model was disposed mid-read — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not read cue badges: {ex.Message}");
        }
    }

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

    // The LIBRARIES tab's single "Scan" action (owner request, 2026-06-30): one click does the whole
    // pass — (1) scan, which analyzes only NEW/CHANGED files (BPM/key/structure/silence cues) and leaves
    // already-analyzed tracks alone, then (2) place automatic hot cues. It deliberately does NOT force a
    // full re-decode of the whole catalog (the old RunRescanAllAsync): force-decoding every track stalls
    // on un-downloaded OneDrive/online-only placeholders and never reached the auto-cue step (the reported
    // hang). The standalone Rescan-all command stays available for a deliberate full re-map. Each step owns
    // its own busy state, status text, and error guard, so a failure in one surfaces without aborting it.
    private async Task RunScanAllAsync()
    {
        await RunScanAsync().ConfigureAwait(false);
        if (_autoCueService is not null)
            await RunAutoCueLibraryAsync().ConfigureAwait(false);
        await RunGenreEnrichmentAsync().ConfigureAwait(false);
    }

    // Gives every never-checked track one online pass (doc 16) — fills missing genres AND cross-checks
    // the detected BPM (a disagreement flags the row red for review) — as the last step of the one-click
    // ScanAll pass. Skipped (with a clear status) when no provider was wired — i.e. no GetSongBPM key.
    // The online lookups run off the UI thread; progress marshals to the status line; cancellation ties to
    // the view-model lifetime. Guarded so a failure surfaces but never blocks scan completion (global #16/#26).
    private async Task RunGenreEnrichmentAsync()
    {
        if (_metadataProvider is null)
        {
            // Append rather than replace, so the preceding auto-cue summary ("… tracks cued") stays visible.
            ScanStatus = $"{ScanStatus} · Online genre/BPM check skipped (no getsongbpm key in Settings).";
            return;
        }

        var service = new Core.Enrichment.CatalogEnrichmentService(
            _library, _metadataProvider, _store,
            onError: e => RxApp.MainThreadScheduler.Schedule(() => ScanStatus = e));

        try
        {
            var progress = new Progress<Core.Enrichment.EnrichmentProgress>(p =>
                RxApp.MainThreadScheduler.Schedule(() =>
                {
                    ScanStatus = p.Done >= p.Total
                        ? $"Online check: {p.Enriched} of {p.Total} tracks matched (genre/BPM)"
                        : $"Checking genre + BPM online… {p.Done}/{p.Total}";
                    if (p.Done >= p.Total)
                        RefreshRows();
                }));

            await Task.Run(() => service.RunAsync(progress, _lifetime.Token), _lifetime.Token).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(RefreshRows);
        }
        catch (OperationCanceledException)
        {
            // App shutting down / tab disposed — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Online genre/BPM check failed: {ex.Message}");
        }
    }

    private async Task RunScanAsync()
    {
        IsScanning = true;
        ScanProgressValue = 0;
        _scanPersistFailed = false;
        // Snapshot the folder set on the calling thread so the persisted copy matches what was scanned
        // and we never read the UI-owned ObservableCollection off-thread.
        List<string> folders = Folders.ToList();
        // A configured folder whose drive/share is offline (a disconnected mapped network drive, an
        // unlinked OneDrive root) is silently skipped by the enumerator, so a scan over it reports
        // "0 tracks" with no clue why. Detect it up front and warn instead (global #26 — no silent skip).
        List<string> offlineFolders = folders
            .Where(f => !string.IsNullOrWhiteSpace(f) && !System.IO.Directory.Exists(f))
            .ToList();
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatus = p.Total == 0 ? "No new files." : $"Analyzing {p.Done} / {p.Total}…";
                ScanProgressValue = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
                // Reveal already-scanned tracks as they come in, instead of waiting for the whole
                // folder — eager for the first dozen, then every 25 (ShouldRevealDuringScan). The
                // final tick is left to the post-await block so the two refreshes never race inside
                // ApplyFilter's Clear()/Add() (doc 27 B0) — same guard as RunRescanAllAsync.
                if (p.Done < p.Total && ShouldRevealDuringScan(p.Done))
                    RxApp.MainThreadScheduler.Schedule(RefreshRows);
            });

            // Persist each track the moment it is analyzed and drop each removed file as it is seen — the
            // incremental scan (owner ask, 2026-07): a crash / close / network drop mid-scan keeps every
            // track scanned so far, instead of one whole-catalog save at the end that loses it all. Cheap
            // on the per-row store (SQLite). The handlers are self-guarded (ScanAsync forbids throwing).
            await _library.ScanAsync(
                folders, progress, CancellationToken.None,
                onEntryProcessed: PersistScannedTrackAsync,
                onEntryRemoved: DeleteScannedTrackAsync).ConfigureAwait(false);

            // Re-apply the sample designations so newly-scanned files under a samples folder are
            // classified as Samples (reclassifies the catalog in place; no-op when none are set). New
            // files were already classified with this set at analysis time, so no re-persist is needed.
            if (_sampleFolders.Count > 0)
                _library.SetSampleFolders(_sampleFolders);

            List<TrackRowViewModel> rows = BuildRows();

            // Tracks were persisted per-track above; now just record the folder set so the next run
            // restores it.
            await PersistScanFoldersAsync(folders).ConfigureAwait(false);

            // Collection mutations must happen on the UI scheduler (immediate in tests).
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                _all = rows;
                RebuildFacets();
                ApplyFilter();
                ScanStatus = offlineFolders.Count == 0
                    ? $"{rows.Count} tracks"
                    : $"{rows.Count} tracks — {offlineFolders.Count} folder(s) OFFLINE, skipped: " +
                      $"{string.Join("; ", offlineFolders)}. Reconnect the drive/share, then Scan again.";
                ScanProgressValue = 100;
                RefreshFolderStatuses();
                _ = RefreshCuePresenceAsync();
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
                    // Surface freshly-mapped BPM/key as they come in — eager for the first dozen, then
                    // every 25 (ShouldRevealDuringScan). The terminal refresh is owned solely by the
                    // post-await block below, so the final tick does NOT refresh here — otherwise the two
                    // RefreshRows fire back-to-back on different threads (Progress<T> posts to the thread
                    // pool with no sync context) and race inside ApplyFilter's Tracks.Clear()/Add(),
                    // double-listing every row (doc 27 B0).
                    if (p.Done < p.Total && ShouldRevealDuringScan(p.Done))
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

        List<string> allPaths = _library.All
            .Select(t => t.File.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allPaths.Count == 0)
        {
            ScanStatus = "No tracks to auto-cue — scan folders first.";
            return;
        }

        // Skip unreachable files and un-downloaded OneDrive/online-only placeholders BEFORE auto-cue: a
        // single placeholder decode would block the worker thread (a synchronous cloud fetch) and hang the
        // whole pass. Probing existence/attributes is cheap and never downloads. Reported, never silent.
        List<string> paths = allPaths.Where(_isLocallyDecodable).ToList();
        int skipped = allPaths.Count - paths.Count;
        if (paths.Count == 0)
        {
            ScanStatus = $"No reachable tracks to auto-cue — {skipped} skipped (offline / online-only).";
            return;
        }

        IsAutoCueing = true;
        ScanProgressValue = 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            // The skipped suffix is shared by the progress-completion and the final messages so the
            // reported count is the same whichever marshals last (Progress<T> posts with no ordering
            // guarantee relative to the post-await block).
            string skippedSuffix = skipped == 0 ? string.Empty : $", {skipped} skipped (offline / online-only)";
            var progress = new Progress<AutoCueProgress>(p =>
                RxApp.MainThreadScheduler.Schedule(() =>
                {
                    ScanStatus = p.Done >= p.Total
                        ? $"Auto-cue complete — {p.Cued} of {p.Total} tracks cued{skippedSuffix}"
                        : $"Placing auto cues… {p.Done}/{p.Total}";
                    ScanProgressValue = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
                }));

            // Decode + structural analysis is CPU-bound; keep it off the UI thread.
            AutoCueOutcome outcome = await Task.Run(
                () => _autoCueService.RunAsync(paths, progress, linked.Token), linked.Token).ConfigureAwait(false);

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                ScanStatus = $"Auto-cue complete — {outcome.Cued} of {outcome.Considered} tracks cued{skippedSuffix}";
                ScanProgressValue = 100;
                // Cues changed — re-read the cue-presence set so the row CUE badges light up.
                RefreshRows();
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

    // Auto-cue just the selected track (the detail-panel "Auto Hot-Cue" button), then re-read its cues so the
    // list + the waveform markers refresh. Decode runs off the UI thread; an offline file is reported, not
    // hung on (mirrors the library-wide pass). Guarded — a failure surfaces on the status line (global #16/#26).
    private async Task RunAutoCueSelectedAsync()
    {
        string? path = _selectedTrack?.Track.File.Path;
        if (_autoCueService is null || string.IsNullOrWhiteSpace(path))
            return;
        if (!_isLocallyDecodable(path))
        {
            ScanStatus = "Cannot auto-cue — the track file is offline or unreachable.";
            return;
        }

        IsAutoCueing = true;
        try
        {
            ScanStatus = $"Placing auto cues for \"{_selectedTrack!.Title}\"...";
            AutoCueOutcome outcome = await Task.Run(
                () => _autoCueService.RunAsync(new[] { path }, cancellationToken: _lifetime.Token),
                _lifetime.Token).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                ScanStatus = outcome.Cued > 0
                    ? $"Placed auto cues for \"{_selectedTrack?.Title}\"."
                    : "No auto cues placed (could not read the track's structure).";
                // Reload just the selected track's cues → refresh the list + the waveform markers. NOT
                // RefreshRows(): rebuilding the Tracks list drops the ListBox selection, which would clear
                // the cues we just placed (the row CUE badge refreshes on the next scan / re-selection).
                RebuildHotCues();
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

    // Drops a manual hot cue at a clicked 0..1 waveform position: converts the fraction to a sample offset
    // (against the track's real sample rate so the cue time reads correctly), places it in the next free
    // slot, and persists it — creating the track's cue set if it has none yet. A full bank is reported, not
    // silently ignored (global #26). The new cue is manual (IsAuto=false) so re-analysis preserves it.
    private async Task MarkCueAtFractionAsync(double fraction)
    {
        TrackRowViewModel? track = _selectedTrack;
        string? path = track?.Track.File.Path;
        double duration = _selectedOverview?.DurationSeconds ?? 0;
        if (_hotCueStore is null || track is null || string.IsNullOrWhiteSpace(path) || duration <= 0)
            return;

        int sampleRate = track.Track.Metadata?.SampleRateHz is { } hz && hz > 0 ? hz : 44_100;
        long samples = CueSamplesAt(fraction, duration, sampleRate);

        try
        {
            TrackCueRecord? record = await _hotCueStore.LoadAsync(path, _lifetime.Token).ConfigureAwait(false);
            TrackCueSet set = record?.ToCueSet() ?? new TrackCueSet(sampleRate);
            int slot = NextFreeCueSlot(set);
            if (slot < 0)
            {
                RxApp.MainThreadScheduler.Schedule(
                    () => ScanStatus = "All 8 hot-cue slots are full — delete one to add another.");
                return;
            }

            set = set.SetHotCue(slot, samples, isAuto: false);
            await _hotCueStore.SaveAsync(TrackCueRecord.FromCueSet(path, set), _lifetime.Token).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                ScanStatus = $"Hot cue {slot + 1} set.";
                // Reload just the selected track's cues → the new marker appears immediately. NOT
                // RefreshRows(): rebuilding the Tracks list would drop the selection and clear the cue.
                RebuildHotCues();
            });
        }
        catch (OperationCanceledException)
        {
            // The view-model was disposed mid-edit — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not set hot cue: {ex.Message}");
        }
    }

    // Sets the selected track's star rating, persists just that row (cheap per-row upsert), and refreshes
    // the list so the rating column/sort update — keeping the selection so the detail panel stays put.
    // Guarded: a persist failure surfaces on the status line, never crashes (global #16/#26).
    private async Task SetSelectedRatingAsync(int rating)
    {
        string? path = _selectedTrack?.Track.File.Path;
        if (string.IsNullOrWhiteSpace(path))
            return;

        MusicTrack? updated = _library.SetRating(path, rating);
        if (updated is null)
            return;

        if (_store is not null)
        {
            try
            {
                await _store.SaveTrackAsync(updated, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Rating set; saving it failed: {ex.Message}");
            }
        }

        RxApp.MainThreadScheduler.Schedule(() =>
        {
            RefreshRows();
            SelectedTrack = Tracks.FirstOrDefault(
                r => string.Equals(r.Track.File.Path, path, StringComparison.OrdinalIgnoreCase));
        });
    }

    // Records a deck load as a play (bumps play count + last-played) and persists that one row. Fire-and-
    // forget from LoadToDeck: it must not block or disrupt the load. The row's play badge updates on the
    // next list refresh — no RefreshRows here, so loading a track never drops the browse selection.
    private async Task RecordPlayAsync(string path)
    {
        MusicTrack? updated = _library.MarkPlayed(path);
        if (updated is null || _store is null)
            return;
        try
        {
            await _store.SaveTrackAsync(updated, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Play recorded; saving it failed: {ex.Message}");
        }
    }

    /// <summary>The sample offset for a 0..1 track <paramref name="fraction"/> on a track of
    /// <paramref name="durationSeconds"/> at <paramref name="sampleRate"/> Hz; the fraction is clamped to
    /// 0..1 so a click can't land a negative or past-the-end cue. Pure so it unit-tests without a decode.</summary>
    public static long CueSamplesAt(double fraction, double durationSeconds, int sampleRate)
        => (long)Math.Round(Math.Clamp(fraction, 0.0, 1.0) * durationSeconds * sampleRate);

    // The lowest empty hot-cue slot in the set, or -1 when all slots are taken.
    private static int NextFreeCueSlot(TrackCueSet set)
    {
        for (int i = 0; i < set.SlotCount; i++)
            if (set.GetHotCue(i) is null)
                return i;
        return -1;
    }

    /// <summary>
    /// Imports another DJ app's library file: parses it with the named importer, maps tracks + cues +
    /// playlists into Liveolator through the import service (path-remapping against the current catalog),
    /// merges the resulting tracks into the catalog, persists, and refreshes. Parsing + analysis run off
    /// the UI thread; the busy state blocks overlapping scans/imports. Guarded — a failure surfaces on the
    /// status line, never crashes the tab (global standards #16/#26).
    /// </summary>
    /// <param name="formatName">The importer to use (matched against <see cref="ImportFormatNames"/>).</param>
    /// <param name="filePath">The source library file to read.</param>
    /// <param name="policy">How to treat tracks/cues already present (default: non-destructive FillGaps).</param>
    public Task ImportFromFileAsync(
        string formatName, string filePath,
        Core.Library.Import.ImportMergePolicy policy = Core.Library.Import.ImportMergePolicy.FillGaps)
    {
        Core.Library.Import.ILibraryImporter? importer = _importers
            .FirstOrDefault(i => string.Equals(i.FormatName, formatName, StringComparison.OrdinalIgnoreCase));
        if (importer is null || string.IsNullOrWhiteSpace(filePath))
            return Task.CompletedTask;

        return RunImportAsync(importer.FormatName, policy, () =>
        {
            using System.IO.FileStream stream = System.IO.File.OpenRead(filePath);
            return importer.Parse(stream);
        });
    }

    /// <summary>
    /// Imports a folder-based DJ library (e.g. Serato, whose data is spread across the audio files +
    /// a <c>_Serato_</c> folder). Same mapping/merge/persist path as <see cref="ImportFromFileAsync"/>.
    /// </summary>
    public Task ImportFromFolderAsync(
        string formatName, string folderPath,
        Core.Library.Import.ImportMergePolicy policy = Core.Library.Import.ImportMergePolicy.FillGaps)
    {
        Core.Library.Import.IFolderLibraryImporter? importer = _folderImporters
            .FirstOrDefault(i => string.Equals(i.FormatName, formatName, StringComparison.OrdinalIgnoreCase));
        if (importer is null || string.IsNullOrWhiteSpace(folderPath))
            return Task.CompletedTask;

        return RunImportAsync(importer.FormatName, policy, () => importer.Parse(folderPath));
    }

    // Shared import runner for both file- and folder-based sources: parse + map (off the UI thread) →
    // merge the resulting tracks into the catalog (Restore dedups by path, import wins as the later entry)
    // → persist + refresh exactly as a scan does. The busy state blocks overlapping scans/imports.
    private async Task RunImportAsync(
        string formatName, Core.Library.Import.ImportMergePolicy policy, Func<Core.Library.Import.LibraryImport> parse)
    {
        if (_importService is null)
            return;

        IsScanning = true;
        try
        {
            Core.Library.Import.LibraryImportResult result = await Task.Run(async () =>
            {
                Core.Library.Import.LibraryImport parsed = parse();
                return await _importService.ImportAsync(parsed, _library.All, policy, _lifetime.Token)
                    .ConfigureAwait(false);
            }, _lifetime.Token).ConfigureAwait(false);

            _library.Restore(_library.All.Concat(result.TracksToUpsert).ToList());
            await PersistImportedCatalogAsync().ConfigureAwait(false);

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                RefreshRows();
                ScanStatus = $"{formatName} — {result.Summary.Describe()}";
            });
        }
        catch (OperationCanceledException)
        {
            // The view-model was disposed mid-import — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Import failed: {ex.Message}");
        }
        finally
        {
            RxApp.MainThreadScheduler.Schedule(() => IsScanning = false);
        }
    }

    // Persists the catalog after an import. Guarded so a save failure surfaces but never crashes the import.
    private async Task PersistImportedCatalogAsync()
    {
        if (_store is null)
            return;
        try
        {
            await _store.SaveMusicAsync(_library.All).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Import done; saving the catalog failed: {ex.Message}");
        }
    }

    // The incremental-scan persist hooks (passed to MediaLibrary.ScanAsync): each analyzed track is saved
    // the instant it lands, and each removed file is dropped — so partial progress survives a crash/close
    // mid-scan (no whole-folder batch at the end). Self-guarded because ScanAsync forbids a throwing
    // handler; the first failure is surfaced once, and the scan is never aborted (global #16/#26).
    private async Task PersistScannedTrackAsync(MusicTrack track, CancellationToken cancellationToken)
    {
        if (_store is null)
            return;
        try
        {
            await _store.SaveTrackAsync(track, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportScanPersistError(ex);
        }
    }

    private async Task DeleteScannedTrackAsync(string path, CancellationToken cancellationToken)
    {
        if (_store is null)
            return;
        try
        {
            await _store.DeleteTrackAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportScanPersistError(ex);
        }
    }

    private void ReportScanPersistError(Exception ex)
    {
        if (_scanPersistFailed)
            return;
        _scanPersistFailed = true;
        RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Scanning; a track failed to save: {ex.Message}");
    }

    // Records the folder set after a scan (the tracks were already persisted per-track). Guarded: a
    // persistence failure surfaces on the status line but never aborts a completed scan.
    private async Task PersistScanFoldersAsync(IReadOnlyList<string> folders)
    {
        if (_store is null)
            return;

        try
        {
            await _store.SaveScanFoldersAsync(folders).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Scan done; saving the folder list failed: {ex.Message}");
        }
    }

    // Persists the trimmed folder set + drops the pruned tracks after a folder removal (and the
    // sample-folder set when the removed folder had been a samples source). Deletes the pruned tracks
    // per-path rather than re-saving the whole catalog: the per-row store is upsert-only, so only an
    // explicit delete removes them. Guarded so a save failure surfaces on the status line but never
    // crashes the removal.
    private async Task PersistAfterRemoveAsync(bool sampleFoldersChanged, IReadOnlyList<string> prunedPaths)
    {
        if (_store is null)
            return;

        try
        {
            await _store.SaveScanFoldersAsync(Folders.ToList()).ConfigureAwait(false);
            foreach (string path in prunedPaths)
                await _store.DeleteTrackAsync(path).ConfigureAwait(false);
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

        // Remember the selected track's path across the rebuild. Tracks.Clear() below drops the old row
        // instance, so the ListBox nulls its bound selection; a background re-projection (re-analysis /
        // cue-badge refresh builds fresh rows) would then make the selection vanish on its own — the
        // reported "click a track, it deselects a few seconds later". Re-point it at the new row afterwards.
        string? selectedPath = _selectedTrack?.Track.File.Path;

        var rowByTrack = _all.ToDictionary(r => r.Track);
        var filter = new TrackFilter(
            Text: SearchText,
            Artist: SelectedArtist,
            Genre: SelectedGenre,
            MinBpm: ParseBpm(BpmMinText),
            MaxBpm: ParseBpm(BpmMaxText),
            Year: SelectedYear,
            FileType: SelectedFileType,
            Status: SelectedStatus,
            MinDuration: ShowShortClips ? null : MinimumVisibleDuration);

        IReadOnlyList<MusicTrack> filtered = TrackQuery.Apply(rowByTrack.Keys, filter, TrackQuery.MaxResults);
        IReadOnlyList<MusicTrack> ordered = TrackSort.Apply(filtered, SortKey, SortDescending);

        Tracks.Clear();
        foreach (MusicTrack track in ordered)
            Tracks.Add(rowByTrack[track]);

        // Re-select the row for the same track (null when it's now filtered out / gone). A filter-only
        // change keeps the same instances, so this is a no-op; a rebuild swaps instances, so this restores
        // the selection the Clear() above dropped.
        if (selectedPath is not null)
            SelectedTrack = Tracks.FirstOrDefault(
                r => string.Equals(r.Track.File.Path, selectedPath, StringComparison.OrdinalIgnoreCase));

        // Surface how many of the whole catalog are shown, and flag when TrackQuery capped the result
        // (the cap used to drop rows with no indication — doc advisor note).
        int total = _all.Count;
        int shown = Tracks.Count;
        ResultSummary = total == 0
            ? string.Empty
            : shown == total ? $"{total} tracks"
            : shown >= TrackQuery.MaxResults ? $"{shown} of {total} (capped)"
            : $"{shown} of {total}";
    }

    // Recomputes the facet dropdowns from the current catalog (after a scan or restore) and drops any
    // selection that no longer exists, so the pickers never offer a stale value. Must run on the UI
    // scheduler (mutates ObservableCollections); callers already marshal there.
    private void RebuildFacets()
    {
        ShortClipCount = _all.Count(r => r.Track.Duration is { } d && d < MinimumVisibleDuration);

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
            BpmMinText = null;
            BpmMaxText = null;
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

    // Parses a BPM bound the user typed; blank or non-numeric input means "no bound" (the filter simply
    // ignores it) rather than an error — a half-typed value never throws mid-keystroke.
    private static double? ParseBpm(string? text)
        => double.TryParse(text, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out double bpm) && bpm > 0
            ? bpm
            : null;

    private void RebuildMatches()
    {
        HarmonicMatches.Clear();
        if (_selectedTrack is null)
            return;

        foreach (MusicTrack match in _library.HarmonicMatches(_selectedTrack.Track)
                     .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
            HarmonicMatches.Add(new TrackRowViewModel(match, _contextActions));
    }

    // Reloads the selected track's stored hot cues from the cue store (a separate file from the catalog,
    // keyed by track path). Reads are async, so the visible list is cleared immediately and refilled when
    // the load returns; a stale read (selection moved on) is dropped via the sequence guard. A null store
    // (no persistence wired) just leaves the list empty. Guarded — a load failure surfaces, never crashes.
    private void RebuildHotCues()
    {
        int sequence = ++_hotCueLoadSequence;
        ClearHotCues();
        _selectedCueRecord = null;
        UpdateCueMarkers();

        TrackRowViewModel? track = _selectedTrack;
        if (_hotCueStore is null || track is null)
            return;

        _ = LoadHotCuesAsync(track.Track.File.Path, sequence);
    }

    // Fire-and-forget waveform decode for the bottom overview strip. Kept off the UI thread (a real decode
    // is CPU-bound) and guarded by a per-selection sequence so a quick A→B→A change never paints a stale
    // wave. The provider degrades to Empty on failure; markers recompute once the duration is known.
    private void RebuildWaveform()
    {
        int sequence = ++_waveformLoadSequence;
        _selectedOverview = null;
        Waveform = null;
        KickPeaks = null;
        MidPeaks = null;
        HighPeaks = null;
        WaveformStatus = string.Empty;
        UpdateCueMarkers();

        TrackRowViewModel? track = _selectedTrack;
        if (_waveformProvider is null || track is null)
            return;

        // A missing / offline / online-only file (an un-downloaded OneDrive placeholder, a dropped network
        // drive) can't be decoded — BASS fails with FileOpen. Skip the doomed decode and say why, instead of
        // showing a blank strip and logging a warning on every click (global #26; OneDrive-placeholder note).
        string path = track.Track.File.Path;
        if (!_isLocallyDecodable(path))
        {
            WaveformStatus = "Track is offline or online-only — download it (or reconnect the drive) to see its waveform.";
            return;
        }

        WaveformStatus = "Loading waveform…";
        _ = LoadWaveformAsync(path, sequence);
    }

    private async Task LoadWaveformAsync(string trackPath, int sequence)
    {
        try
        {
            WaveformOverview overview = await Task.Run(
                () => _waveformProvider!.GetOverviewAsync(trackPath, WaveformBuckets, _lifetime.Token),
                _lifetime.Token).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                // The selection changed (or another load started) while we were decoding — drop this result.
                if (sequence != _waveformLoadSequence)
                    return;

                _selectedOverview = overview.IsEmpty ? null : overview;
                Waveform = overview.IsEmpty ? null : overview.Peaks;
                KickPeaks = overview.IsEmpty ? null : overview.LowPeaks;
                MidPeaks = overview.IsEmpty ? null : overview.MidPeaks;
                HighPeaks = overview.IsEmpty ? null : overview.HighPeaks;
                WaveformStatus = overview.IsEmpty ? "Waveform unavailable for this track." : string.Empty;
                UpdateCueMarkers();
            });
        }
        catch (OperationCanceledException)
        {
            // The view-model was disposed mid-load — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                if (sequence == _waveformLoadSequence)
                    WaveformStatus = "Could not load the waveform.";
                ScanStatus = $"Could not load waveform: {ex.Message}";
            });
        }
    }

    // Recompute the waveform's hot-cue markers from the loaded overview + cue record (both async, either can
    // arrive first). Needs the decoded duration to map a cue's sample offset onto the strip's 0..1 axis.
    private void UpdateCueMarkers()
        => HotCueMarkers = _selectedOverview is { } overview && _selectedCueRecord is { } record
            ? CueMarkerFractions(record, overview.DurationSeconds)
            : null;

    /// <summary>
    /// Maps each stored hot cue to its 0..1 position along a track of <paramref name="durationSeconds"/>,
    /// dropping cues outside the track (or when the duration/sample-rate is unknown). Pure so the mapping
    /// unit-tests without a decode.
    /// </summary>
    public static IReadOnlyList<double> CueMarkerFractions(TrackCueRecord record, double durationSeconds)
    {
        if (durationSeconds <= 0 || record.SampleRate <= 0)
            return Array.Empty<double>();

        return record.HotCues
            .Select(c => c.PositionSamples / (double)record.SampleRate / durationSeconds)
            .Where(f => f is >= 0 and <= 1)
            .ToList();
    }

    private async Task LoadHotCuesAsync(string trackPath, int sequence)
    {
        try
        {
            TrackCueRecord? record = await _hotCueStore!.LoadAsync(trackPath, _lifetime.Token).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                // The selection changed (or another load started) while we were reading — drop this result.
                if (sequence != _hotCueLoadSequence)
                    return;

                ClearHotCues();
                _selectedCueRecord = record;
                if (record is null)
                {
                    UpdateCueMarkers();
                    return;
                }

                foreach (HotCue cue in record.HotCues.OrderBy(c => c.Index))
                    HotCues.Add(new HotCueDisplayViewModel(cue, record.SampleRate, ConfirmHotCue, DeleteHotCue));
                this.RaisePropertyChanged(nameof(HasHotCues));
                UpdateCueMarkers();
            });
        }
        catch (OperationCanceledException)
        {
            // The view-model was disposed mid-load — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not load hot cues: {ex.Message}");
        }
    }

    private void ClearHotCues()
    {
        if (HotCues.Count == 0)
            return;
        HotCues.Clear();
        this.RaisePropertyChanged(nameof(HasHotCues));
    }

    // Commit a suggested (auto) cue to a manual one in the store, so re-analysis preserves it verbatim
    // (the owner's suggested → commit rule, 2026-06-19). A no-op for a slot that is empty or already manual.
    private void ConfirmHotCue(int index) => _ = MutateSelectedCuesAsync(set =>
        set.GetHotCue(index) is { IsAuto: true } cue
            ? set.SetHotCue(index, cue.PositionSamples, cue.Label, cue.Color, isAuto: false)
            : set);

    // Reject/remove a stored cue (a suggestion the DJ doesn't want, or any cue) from the track's cue set.
    private void DeleteHotCue(int index) => _ = MutateSelectedCuesAsync(set => set.ClearHotCue(index));

    // Applies a pure transform to the selected track's persisted cue set and saves it, then refreshes the
    // shown list. The edit is to the stored cues (the catalog of jump points); a deck currently holding the
    // track picks the change up on its next load (or an explicit re-apply). Guarded — a store failure
    // surfaces on the status line, never crashes the tab (global standards #16/#26).
    private async Task MutateSelectedCuesAsync(Func<TrackCueSet, TrackCueSet> transform)
    {
        string? path = _selectedTrack?.Track.File.Path;
        if (_hotCueStore is null || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            TrackCueRecord? record = await _hotCueStore.LoadAsync(path, _lifetime.Token).ConfigureAwait(false);
            if (record is null)
                return;

            TrackCueSet updated = transform(record.ToCueSet());
            await _hotCueStore.SaveAsync(TrackCueRecord.FromCueSet(path, updated), _lifetime.Token).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(RebuildHotCues);
        }
        catch (OperationCanceledException)
        {
            // The view-model was disposed mid-edit — nothing to do.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not update hot cues: {ex.Message}");
        }
    }

    // Load + play the selected track via the action layer (doc 04) — the UI never touches the engine.
    // Same load-or-queue policy as the deck loads: a playing deck A queues the track instead of cutting
    // it off, and an unreachable file reports why instead of failing deep in the engine (global #26).
    private void PlaySelected()
    {
        if (_dispatcher is null || _deckLoader is null || _selectedTrack is null)
            return;

        // The library "Play" is an AUDITION: the user asked to hear THIS track now, so replace whatever is
        // on Deck A rather than queueing behind it (the default loader policy queues a playing deck, which
        // made a second Play do nothing — the reported "it ignores me" bug). The engine leaves a freshly
        // loaded deck paused, so we then play it — but only if it isn't already playing (DeckPlayPause is a
        // toggle, so an unconditional dispatch could pause a deck the load left running).
        Core.Playlist.DeckLoadResult result = _deckLoader.Load(
            slot: 0,
            _selectedTrack.Track.File.Path,
            bpm: _selectedTrack.Track.Bpm?.Bpm ?? 0, // analyzed BPM → deck sync reference (doc 11)
            firstBeatSeconds: _selectedTrack.Track.Bpm?.FirstBeatSeconds ?? 0, // downbeat anchor → phase-match (doc 22 A1)
            replacePlaying: true);
        if (result.Outcome == Core.Playlist.DeckLoadOutcome.Loaded
            && !_dispatcher.GetFeedback(PerformanceActionKind.DeckPlayPause, 0).IsActive)
            _dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause));
        LoadStatus = result.Message;

        // An audition counts as a play (play count + last-played), persisted per-row.
        if (result.Outcome == Core.Playlist.DeckLoadOutcome.Loaded)
            _ = RecordPlayAsync(_selectedTrack.Track.File.Path);
    }

    private void Stop()
        => _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.TransportStop));

    // Stage the selected track on a deck slot (A = 0, B = 1) via the action layer — no auto-play
    // (load ≠ play; the performer beat-matches, then brings the deck in). A playing deck queues the
    // track instead; an unreachable file dispatches nothing and reports why (global #26).
    /// <summary>Loads the selected track onto Deck A — the double-click-to-load action every DJ browser
    /// has. No-op when nothing is selected or deck A isn't backed by the engine.</summary>
    public void LoadSelectedToDeckA()
    {
        if (SelectedTrack is not null && CanLoadToDeckA)
            LoadToDeck(0);
    }

    private void LoadToDeck(int slot)
    {
        if (_deckLoader is null || _selectedTrack is null)
            return;

        string path = _selectedTrack.Track.File.Path;
        LoadStatus = _deckLoader.Load(
            slot,
            path,
            bpm: _selectedTrack.Track.Bpm?.Bpm ?? 0, // analyzed BPM → deck sync reference (doc 11)
            firstBeatSeconds: _selectedTrack.Track.Bpm?.FirstBeatSeconds ?? 0,
            kickOnsetsSeconds: _selectedTrack.Track.Bpm?.KickOnsetsSeconds).Message; // doc 22 A1

        // Count the load as a play (play count + last-played), persisted per-row. Fire-and-forget so it
        // never delays the load.
        _ = RecordPlayAsync(path);
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
