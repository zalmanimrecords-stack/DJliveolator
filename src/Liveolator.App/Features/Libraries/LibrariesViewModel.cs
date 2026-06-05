using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
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
    private List<TrackRowViewModel> _all = new();
    private string? _searchText;
    private TrackRowViewModel? _selectedTrack;
    private string _scanStatus = "Add folders, then Scan.";
    private bool _isScanning;
    private string _liveBpm = "—";

    /// <param name="dispatcher">Action layer for playback intent; null disables Live Mode playback.</param>
    /// <param name="beatClock">Live beat clock to read the detected tempo from; null when Live Mode is off.</param>
    public LibrariesViewModel(
        MusicLibrary library,
        IPerformanceActionDispatcher? dispatcher = null,
        IBeatClock? beatClock = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _dispatcher = dispatcher;
        _beatClock = beatClock;

        ScanCommand = ReactiveCommand.CreateFromTask(
            RunScanAsync,
            this.WhenAnyValue(x => x.IsScanning, scanning => !scanning));

        IObservable<bool> canPlay = this.WhenAnyValue(x => x.SelectedTrack)
            .Select(track => track is not null && _dispatcher is not null);
        PlaySelectedCommand = ReactiveCommand.Create(PlaySelected, canPlay);

        StopCommand = ReactiveCommand.Create(Stop);

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

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }
    public ReactiveCommand<Unit, Unit> PlaySelectedCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    /// <summary>True when playback is wired (Live Mode on); the UI hides transport controls otherwise.</summary>
    public bool IsLiveModeEnabled => _dispatcher is not null;

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

    /// <summary>Adds a folder root to scan (no-op if blank or already present).</summary>
    public void AddFolder(string folder)
    {
        if (!string.IsNullOrWhiteSpace(folder) && !Folders.Contains(folder))
            Folders.Add(folder);
    }

    private async Task RunScanAsync()
    {
        IsScanning = true;
        try
        {
            var progress = new Progress<ScanProgress>(p =>
                ScanStatus = p.Total == 0 ? "No new files." : $"Analyzing {p.Done} / {p.Total}…");

            await _library.ScanAsync(Folders.ToList(), progress).ConfigureAwait(false);

            List<TrackRowViewModel> rows = _library.All
                .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .Select(t => new TrackRowViewModel(t))
                .ToList();

            // Collection mutations must happen on the UI scheduler (immediate in tests).
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                _all = rows;
                ApplyFilter();
                ScanStatus = $"{rows.Count} tracks";
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

    private void OnBeatStateChanged(object? sender, BeatClockState state)
        => RxApp.MainThreadScheduler.Schedule(() => UpdateLiveBpm(state));

    private void UpdateLiveBpm(BeatClockState state)
        => LiveBpm = state.Bpm > 0 ? $"{state.Bpm:0.0} BPM" : "—";
}
