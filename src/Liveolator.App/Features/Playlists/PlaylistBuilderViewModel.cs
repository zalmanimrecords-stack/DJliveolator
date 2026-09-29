using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Shell;
using Liveolator.Core;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Playlist.LocalCopy;
using Liveolator.Core.Settings;
using ReactiveUI;

namespace Liveolator.App.Features.Playlists;

/// <summary>
/// Builds a named, saved playlist / set (a "crate"): curate tracks from the library — manually
/// (Add / reorder / remove) or by harmonic auto-fill from a seed (<see cref="HarmonicSetBuilder"/>) —
/// then Save (via <see cref="IPlaylistStore"/>) and optionally push it to the live set
/// (<see cref="ILivePlaylist"/>). Holds no Avalonia types; all store calls are guarded and surfaced
/// on <see cref="Status"/> (never silent — global standards #16/#26).
/// </summary>
public sealed class PlaylistBuilderViewModel : ViewModelBase
{
    private const int AutoFillLength = 12;

    private readonly MusicLibrary _library;
    private readonly IPlaylistStore _store;
    private readonly ILivePlaylist? _livePlaylist;
    private readonly Shared.TrackContextActions? _contextActions;
    private readonly LocalCopyWiring? _localCopy;
    private readonly HarmonicSetBuilder _setBuilder = new();
    private CancellationTokenSource? _copyCancellation;
    private bool _isCopying;
    private double _copyProgress;
    private List<TrackRowViewModel> _allLibrary = new();

    private string _name = "New playlist";
    private string? _librarySearch;
    private string? _selectedSaved;
    private TrackRowViewModel? _selectedLibraryTrack;
    private PlaylistTrackViewModel? _selectedCurrent;
    private string _status = string.Empty;

    public PlaylistBuilderViewModel(
        MusicLibrary library,
        IPlaylistStore store,
        ILivePlaylist? livePlaylist = null,
        Shared.TrackContextActions? contextActions = null,
        LocalCopyWiring? localCopy = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _livePlaylist = livePlaylist;
        _contextActions = contextActions;
        _localCopy = localCopy;

        var hasName = this.WhenAnyValue(x => x.Name).Select(n => !string.IsNullOrWhiteSpace(n));
        var hasSaved = this.WhenAnyValue(x => x.SelectedSaved).Select(s => !string.IsNullOrWhiteSpace(s));
        var hasLibrarySelection = this.WhenAnyValue(x => x.SelectedLibraryTrack).Select(t => t is not null);
        var hasCurrentSelection = this.WhenAnyValue(x => x.SelectedCurrent).Select(t => t is not null);

        NewCommand = ReactiveCommand.Create(NewPlaylist);
        AddTrackCommand = ReactiveCommand.Create(AddSelectedLibraryTrack, hasLibrarySelection);
        RemoveCommand = ReactiveCommand.Create(RemoveSelectedCurrent, hasCurrentSelection);
        MoveUpCommand = ReactiveCommand.Create(() => MoveCurrent(-1), hasCurrentSelection);
        MoveDownCommand = ReactiveCommand.Create(() => MoveCurrent(+1), hasCurrentSelection);
        AutoFillCommand = ReactiveCommand.Create(AutoFill, hasLibrarySelection);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, hasName);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync, hasSaved);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync, hasSaved);

        SendToLiveSetCommand = ReactiveCommand.Create(
            SendToLiveSet,
            this.WhenAnyValue(x => x.Current.Count).Select(c => c > 0 && _livePlaylist is not null));

        CancelCopyCommand = ReactiveCommand.Create(
            () => _copyCancellation?.Cancel(), this.WhenAnyValue(x => x.IsCopying));
        Current.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(CanCopyToComputer));

        this.WhenAnyValue(x => x.LibrarySearch).Subscribe(_ => ApplyLibraryFilter());
    }

    public ObservableCollection<TrackRowViewModel> Library { get; } = new();
    public ObservableCollection<PlaylistTrackViewModel> Current { get; } = new();
    public ObservableCollection<string> SavedPlaylists { get; } = new();

    public ReactiveCommand<Unit, Unit> NewCommand { get; }
    public ReactiveCommand<Unit, Unit> AddTrackCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveUpCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveDownCommand { get; }
    public ReactiveCommand<Unit, Unit> AutoFillCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> SendToLiveSetCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCopyCommand { get; }

    /// <summary>True when a live queue is wired (drives the "Send to live set" button).</summary>
    public bool CanSendToLiveSet => _livePlaylist is not null;

    /// <summary>True when copying to this computer is wired at all (drives the button's visibility).</summary>
    public bool IsLocalCopyAvailable => _localCopy is not null;

    /// <summary>True when the current set can be copied now (drives the button's enabled state).</summary>
    public bool CanCopyToComputer => _localCopy is not null && !IsCopying && Current.Count > 0;

    public bool IsCopying
    {
        get => _isCopying;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isCopying, value);
            this.RaisePropertyChanged(nameof(CanCopyToComputer));
        }
    }

    /// <summary>Copy progress, 0–100.</summary>
    public double CopyProgress
    {
        get => _copyProgress;
        private set => this.RaiseAndSetIfChanged(ref _copyProgress, value);
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string? LibrarySearch
    {
        get => _librarySearch;
        set => this.RaiseAndSetIfChanged(ref _librarySearch, value);
    }

    public string? SelectedSaved
    {
        get => _selectedSaved;
        set => this.RaiseAndSetIfChanged(ref _selectedSaved, value);
    }

    public TrackRowViewModel? SelectedLibraryTrack
    {
        get => _selectedLibraryTrack;
        set => this.RaiseAndSetIfChanged(ref _selectedLibraryTrack, value);
    }

    public PlaylistTrackViewModel? SelectedCurrent
    {
        get => _selectedCurrent;
        set => this.RaiseAndSetIfChanged(ref _selectedCurrent, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>Loads the library snapshot for the picker and the list of saved playlists. Called when opened.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _allLibrary = _library.All
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TrackRowViewModel(t, _contextActions))
            .ToList();
        ApplyLibraryFilter();
        await RefreshSavedAsync(cancellationToken).ConfigureAwait(false);
    }

    private void NewPlaylist()
    {
        Current.Clear();
        SelectedCurrent = null;
        Name = "New playlist";
        Status = "New playlist — add tracks, then Save.";
    }

    private void AddSelectedLibraryTrack()
    {
        if (SelectedLibraryTrack is not { } row)
            return;
        string path = row.Track.File.Path;
        if (Current.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
            return; // dedupe — a set holds each track once
        Current.Add(PlaylistTrackViewModel.From(path, row.Track, _contextActions));
    }

    private void RemoveSelectedCurrent()
    {
        if (SelectedCurrent is { } entry)
            Current.Remove(entry);
    }

    private void MoveCurrent(int delta)
    {
        if (SelectedCurrent is not { } entry)
            return;
        int i = Current.IndexOf(entry);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= Current.Count)
            return;
        Current.Move(i, j);
        SelectedCurrent = entry; // keep selection on the moved row
    }

    private void AutoFill()
    {
        if (SelectedLibraryTrack is not { } seedRow)
            return;
        if (seedRow.Track.Key is null)
        {
            Status = "Seed track has no detected key — pick a keyed track to auto-fill.";
            return;
        }

        HarmonicSet set = _setBuilder.Build(
            seedRow.Track,
            _library.All,
            new HarmonicSetOptions(AutoFillLength));

        Current.Clear();
        foreach (SetEntry entry in set.Entries)
            Current.Add(PlaylistTrackViewModel.From(entry.Track.File.Path, entry.Track, _contextActions));
        Status = $"Auto-filled {Current.Count} tracks from \"{seedRow.Title}\".";
    }

    private async Task SaveAsync()
    {
        try
        {
            var playlist = new Playlist(Name.Trim(), Current.Select(e => e.Path).ToList());
            await _store.SaveAsync(playlist).ConfigureAwait(false);
            await RefreshSavedAsync().ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Saved \"{playlist.Name}\" ({playlist.TrackPaths.Count} tracks).");
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Save failed: {ex.Message}");
        }
    }

    private async Task OpenAsync()
    {
        if (SelectedSaved is not { } name)
            return;
        try
        {
            Playlist? playlist = await _store.LoadAsync(name).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                if (playlist is null)
                {
                    Status = $"Could not open \"{name}\".";
                    return;
                }
                LoadIntoCurrent(playlist);
                Status = $"Opened \"{playlist.Name}\" ({playlist.TrackPaths.Count} tracks).";
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Open failed: {ex.Message}");
        }
    }

    private async Task DeleteAsync()
    {
        if (SelectedSaved is not { } name)
            return;
        try
        {
            await _store.DeleteAsync(name).ConfigureAwait(false);
            await RefreshSavedAsync().ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Deleted \"{name}\".");
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Delete failed: {ex.Message}");
        }
    }

    /// <summary>The folder the last copy went to, offered as the picker's starting point; null when none yet.</summary>
    public async Task<string?> LastLocalCopyFolderAsync()
        => _localCopy is null ? null : (await _localCopy.Settings.LoadAsync().ConfigureAwait(false)).LocalCopyFolder;

    /// <summary>
    /// Copies the current set's files to <paramref name="destination"/> for a gig away from the NAS, and
    /// saves a "(local)" playlist pointing at the copies. The destination is picked by the view.
    /// </summary>
    public async Task CopyToComputerAsync(string destination)
    {
        if (!CanCopyToComputer)
            return;
        if (string.IsNullOrWhiteSpace(destination))
        {
            Status = "That folder has no local path. Choose a folder on this computer's disk.";
            return;
        }

        LocalCopyWiring wiring = _localCopy!;
        var playlist = new Playlist(Name.Trim(), Current.Select(e => e.Path).ToList());
        IReadOnlyList<string> folders = wiring.LibraryFolders();
        IReadOnlyCollection<MusicTrack> catalog = _library.All;
        using var cancellation = new CancellationTokenSource();
        _copyCancellation = cancellation;
        IsCopying = true;
        CopyProgress = 0;
        Status = $"Copying \"{playlist.Name}\" to {destination}…";
        var progress = new MainThreadProgress(p =>
        {
            CopyProgress = p.Total == 0 ? 100 : 100.0 * p.Done / p.Total;
            if (p.Done < p.Total)
                Status = $"Copying {p.Done + 1} / {p.Total}: {PortablePath.GetFileName(p.CurrentFile)}";
        });

        try
        {
            LocalCopyResult result = await Task.Run(
                () => wiring.Service.CopyAsync(playlist, catalog, folders, destination, progress, cancellation.Token),
                cancellation.Token).ConfigureAwait(false);

            if (result.InsufficientSpace)
            {
                RxApp.MainThreadScheduler.Schedule(() => Status =
                    $"Not enough space at {destination}: needs {Gigabytes(result.BytesToCopy)}, "
                    + $"{Gigabytes(result.BytesFree)} free. Nothing was copied.");
                return;
            }

            RxApp.MainThreadScheduler.Schedule(() => wiring.AdoptCopies(destination, result.LocalTracks));
            await RememberFolderAsync(wiring.Settings, destination).ConfigureAwait(false);
            await RefreshSavedAsync().ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() => Status = Describe(result));
        }
        catch (OperationCanceledException)
        {
            // Finished files already have their rows saved; registering the folder keeps them from being
            // pruned before the re-run that completes the set.
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                wiring.AdoptCopies(destination, Array.Empty<MusicTrack>());
                Status = "Copy cancelled. Files already copied are kept; copy again to finish.";
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Copy failed: {ex.Message}");
        }
        finally
        {
            _copyCancellation = null;
            RxApp.MainThreadScheduler.Schedule(() => IsCopying = false);
        }
    }

    private async Task RememberFolderAsync(ISettingsStore settings, string folder)
    {
        try
        {
            AppSettings current = await settings.LoadAsync().ConfigureAwait(false);
            await settings.SaveAsync(current with { LocalCopyFolder = folder }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => Status = $"Copied, but remembering the folder failed: {ex.Message}");
        }
    }

    private static string Describe(LocalCopyResult result)
    {
        string summary = $"Copied {result.Copied}, {result.AlreadyPresent} already on this computer";
        if (result.LocalPlaylist is { } local)
            summary += $". Saved \"{local.Name}\"";
        if (result.Problems.Count > 0)
            summary += $". {result.Problems.Count} left out: "
                + string.Join("; ", result.Problems.Take(3).Select(p => $"{PortablePath.GetFileName(p.SourcePath)} ({p.Reason})"))
                + (result.Problems.Count > 3 ? "; …" : string.Empty);
        return summary + ".";
    }

    private static string Gigabytes(long bytes) => $"{bytes / 1e9:0.0} GB";

    // Progress<T> posts to whatever context built it; routing through the main-thread scheduler keeps the
    // updates on the UI thread in the app and synchronous in tests, and ordered before the final status.
    private sealed class MainThreadProgress(Action<ScanProgress> onReport) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => RxApp.MainThreadScheduler.Schedule(() => onReport(value));
    }

    private void SendToLiveSet()
    {
        if (_livePlaylist is null)
            return;
        _livePlaylist.Load(Current.Select(e => e.Path));
        Status = $"Sent {Current.Count} tracks to the live set.";
    }

    private void LoadIntoCurrent(Playlist playlist)
    {
        var byPath = _library.All.ToDictionary(
            t => t.File.Path, t => t, StringComparer.OrdinalIgnoreCase);

        Current.Clear();
        foreach (string path in playlist.TrackPaths)
            Current.Add(PlaylistTrackViewModel.From(path, byPath.GetValueOrDefault(path), _contextActions));

        Name = playlist.Name;
        SelectedCurrent = null;
    }

    private async Task RefreshSavedAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> names = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            SavedPlaylists.Clear();
            foreach (string name in names)
                SavedPlaylists.Add(name);
        });

        // Keep the shared right-click "Add to playlist" submenu in sync with new/removed sets.
        if (_contextActions is not null)
            await _contextActions.RefreshPlaylistsAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ApplyLibraryFilter()
    {
        Library.Clear();
        string? query = LibrarySearch?.Trim();
        foreach (TrackRowViewModel row in _allLibrary)
            if (string.IsNullOrEmpty(query) || row.Matches(query))
                Library.Add(row);
    }
}
