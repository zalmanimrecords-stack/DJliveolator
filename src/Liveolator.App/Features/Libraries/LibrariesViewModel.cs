using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using ReactiveUI;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// The Libraries tab. Connects the UI to the real <see cref="MusicLibrary"/> Core module:
/// adds folders, runs the (incremental, background) scan, and exposes the analyzed tracks,
/// search filtering, selection, and Camelot harmonic matches. Holds no Avalonia types.
/// </summary>
public sealed class LibrariesViewModel : ViewModelBase
{
    private readonly MusicLibrary _library;
    private List<TrackRowViewModel> _all = new();
    private string? _searchText;
    private TrackRowViewModel? _selectedTrack;
    private string _scanStatus = "Add folders, then Scan.";
    private bool _isScanning;

    public LibrariesViewModel(MusicLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));

        ScanCommand = ReactiveCommand.CreateFromTask(
            RunScanAsync,
            this.WhenAnyValue(x => x.IsScanning, scanning => !scanning));

        this.WhenAnyValue(x => x.SearchText).Subscribe(_ => ApplyFilter());
        this.WhenAnyValue(x => x.SelectedTrack).Subscribe(_ => RebuildMatches());
    }

    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = new();
    public ObservableCollection<TrackRowViewModel> HarmonicMatches { get; } = new();

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }

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
}
