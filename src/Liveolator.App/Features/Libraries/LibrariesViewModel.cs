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
    private List<TrackRowViewModel> _all = new();
    private string? _searchText;
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
        Playlists.PlaylistBuilderViewModel? playlistBuilder = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _dispatcher = dispatcher;
        _beatClock = beatClock;
        _store = store;
        PlaylistBuilder = playlistBuilder;

        ScanCommand = ReactiveCommand.CreateFromTask(
            RunScanAsync,
            this.WhenAnyValue(x => x.IsScanning, scanning => !scanning));

        IObservable<bool> canPlay = this.WhenAnyValue(x => x.SelectedTrack)
            .Select(track => track is not null && _dispatcher is not null);
        PlaySelectedCommand = ReactiveCommand.Create(PlaySelected, canPlay);

        StopCommand = ReactiveCommand.Create(Stop);

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

        this.WhenAnyValue(x => x.SearchText).Subscribe(_ => ApplyFilter());
        this.WhenAnyValue(x => x.SelectedTrack).Subscribe(_ => RebuildMatches());
    }

    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = new();
    public ObservableCollection<TrackRowViewModel> HarmonicMatches { get; } = new();

    /// <summary>Per-folder scan/update status (one row per added folder) for the folder-status window.</summary>
    public ObservableCollection<FolderStatusViewModel> FolderStatuses { get; } = new();

    /// <summary>The playlist/set builder opened from the "Playlists" button; null disables the button.</summary>
    public Playlists.PlaylistBuilderViewModel? PlaylistBuilder { get; }

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }
    public ReactiveCommand<Unit, Unit> PlaySelectedCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadToDeckACommand { get; }
    public ReactiveCommand<Unit, Unit> LoadToDeckBCommand { get; }

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
                    .Select(t => new TrackRowViewModel(t))
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
                .Select(t => new TrackRowViewModel(t))
                .ToList();

            // Persist the fresh catalog + the folders that produced it, so the next run restores them.
            await PersistCatalogAsync(folders).ConfigureAwait(false);

            // Collection mutations must happen on the UI scheduler (immediate in tests).
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                _all = rows;
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

    private void ApplyFilter()
    {
        Tracks.Clear();
        string? query = SearchText?.Trim();
        foreach (TrackRowViewModel row in _all)
            if (string.IsNullOrEmpty(query) || row.Matches(query))
                Tracks.Add(row);
    }

    private void RebuildMatches()
    {
        HarmonicMatches.Clear();
        if (_selectedTrack is null)
            return;

        foreach (MusicTrack match in _library.HarmonicMatches(_selectedTrack.Track)
                     .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
            HarmonicMatches.Add(new TrackRowViewModel(match));
    }

    // Load + play the selected track via the action layer (doc 04) — the UI never touches the engine.
    private void PlaySelected()
    {
        if (_dispatcher is null || _selectedTrack is null)
            return;

        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, Argument: _selectedTrack.Track.File.Path));
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
            PerformanceActionKind.DeckLoadTrack, Slot: slot, Argument: _selectedTrack.Track.File.Path));
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
