using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Shared;
using Liveolator.App.Shell;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio;
using Liveolator.Core.Waveform;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// The STUDIO tab: a pre-show set planner. Curate tracks from the library, auto-order them
/// harmonically (<see cref="StudioSetPlanner"/>), tune the transition between each pair on a
/// timeline, then Save (<see cref="IStudioSetStore"/>) or push to the live set
/// (<see cref="ILivePlaylist"/>). Holds no Avalonia types and no domain logic beyond presentation;
/// every store call is guarded and surfaced on <see cref="Status"/> (never silent — global #16/#26).
/// </summary>
public sealed class StudioViewModel : ViewModelBase
{
    private const int AutoBuildLength = 12;
    private const int WaveformBuckets = 512;

    private readonly MusicLibrary _library;
    private readonly IStudioSetStore _store;
    private readonly StudioSetPlanner _planner;
    private readonly IWaveformProvider? _waveforms;
    private readonly ILivePlaylist? _live;
    private readonly TrackContextActions? _contextActions;
    private readonly Dictionary<string, MusicTrack> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private List<TrackRowViewModel> _allLibrary = new();

    private string _name = "New set";
    private string? _librarySearch;
    private string? _selectedSaved;
    private TrackRowViewModel? _selectedLibraryTrack;
    private StudioEntryViewModel? _selectedEntry;
    private string _status = string.Empty;

    public StudioViewModel(
        MusicLibrary library,
        IStudioSetStore store,
        StudioSetPlanner? planner = null,
        IWaveformProvider? waveforms = null,
        ILivePlaylist? live = null,
        TrackContextActions? contextActions = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _planner = planner ?? new StudioSetPlanner();
        _waveforms = waveforms;
        _live = live;
        _contextActions = contextActions;

        var hasName = this.WhenAnyValue(x => x.Name).Select(n => !string.IsNullOrWhiteSpace(n));
        var hasSaved = this.WhenAnyValue(x => x.SelectedSaved).Select(s => !string.IsNullOrWhiteSpace(s));
        var hasLibrarySelection = this.WhenAnyValue(x => x.SelectedLibraryTrack).Select(t => t is not null);
        var hasEntrySelection = this.WhenAnyValue(x => x.SelectedEntry).Select(e => e is not null);

        NewCommand = ReactiveCommand.Create(NewSet);
        AddTrackCommand = ReactiveCommand.Create(AddSelectedLibraryTrack, hasLibrarySelection);
        AutoBuildCommand = ReactiveCommand.Create(AutoBuild, hasLibrarySelection);
        RemoveCommand = ReactiveCommand.Create(RemoveSelectedEntry, hasEntrySelection);
        MoveUpCommand = ReactiveCommand.Create(() => MoveSelected(-1), hasEntrySelection);
        MoveDownCommand = ReactiveCommand.Create(() => MoveSelected(+1), hasEntrySelection);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, hasName);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync, hasSaved);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync, hasSaved);
        SendToLiveSetCommand = ReactiveCommand.Create(
            SendToLiveSet,
            this.WhenAnyValue(x => x.Entries.Count).Select(c => c > 0 && _live is not null));

        this.WhenAnyValue(x => x.LibrarySearch).Subscribe(_ => ApplyLibraryFilter());
    }

    public ObservableCollection<TrackRowViewModel> Library { get; } = new();
    public ObservableCollection<StudioEntryViewModel> Entries { get; } = new();
    public ObservableCollection<string> SavedSets { get; } = new();

    public ReactiveCommand<Unit, Unit> NewCommand { get; }
    public ReactiveCommand<Unit, Unit> AddTrackCommand { get; }
    public ReactiveCommand<Unit, Unit> AutoBuildCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveUpCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveDownCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> SendToLiveSetCommand { get; }

    /// <summary>True when a live queue is wired (drives the "Send to live set" button).</summary>
    public bool CanSendToLiveSet => _live is not null;

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

    /// <summary>The lane whose transition the inspector edits.</summary>
    public StudioEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedEntry, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>Loads the library snapshot for the picker and the saved-set list. Called when opened.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _byPath.Clear();
        foreach (MusicTrack track in _library.All)
            _byPath[track.File.Path] = track;

        _allLibrary = _library.All
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TrackRowViewModel(t, _contextActions))
            .ToList();
        ApplyLibraryFilter();
        await RefreshSavedAsync(cancellationToken).ConfigureAwait(false);
    }

    private void NewSet()
    {
        Entries.Clear();
        SelectedEntry = null;
        Name = "New set";
        Status = "New set — add tracks or auto-build from a seed, then Save.";
    }

    private void AddSelectedLibraryTrack()
    {
        if (SelectedLibraryTrack is not { } row)
            return;
        string path = row.Track.File.Path;
        if (Entries.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
            return; // a set holds each track once

        Entries.Add(new StudioEntryViewModel(path, row.Track, transitionIn: null));
        NormalizeTransitions();
        LoadWaveform(Entries[^1]);
    }

    private void AutoBuild()
    {
        if (SelectedLibraryTrack is not { } seedRow)
            return;
        if (seedRow.Track.Key is null)
        {
            Status = "Seed track has no detected key — pick a keyed track to auto-build.";
            return;
        }

        StudioSet set = _planner.BuildFrom(
            Name, seedRow.Track, _library.All, new HarmonicSetOptions(AutoBuildLength));
        RebuildEntriesFrom(set);
        Status = $"Auto-built {Entries.Count} tracks from \"{seedRow.Title}\".";
    }

    private void RemoveSelectedEntry()
    {
        if (SelectedEntry is { } entry)
        {
            Entries.Remove(entry);
            SelectedEntry = null;
            NormalizeTransitions();
        }
    }

    private void MoveSelected(int delta)
    {
        if (SelectedEntry is not { } entry)
            return;
        int i = Entries.IndexOf(entry);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= Entries.Count)
            return;
        Entries.Move(i, j);
        SelectedEntry = entry; // keep selection on the moved lane
        NormalizeTransitions();
    }

    private async Task SaveAsync()
    {
        try
        {
            var set = new StudioSet(Name.Trim(), Entries.Select(e => e.ToModel()).ToList());
            await _store.SaveAsync(set).ConfigureAwait(false);
            await RefreshSavedAsync().ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(
                () => Status = $"Saved \"{set.Name}\" ({set.Entries.Count} tracks).");
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
            StudioSet? set = await _store.LoadAsync(name).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                if (set is null)
                {
                    Status = $"Could not open \"{name}\".";
                    return;
                }
                RebuildEntriesFrom(set);
                Name = set.Name;
                Status = $"Opened \"{set.Name}\" ({set.Entries.Count} tracks).";
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

    private void SendToLiveSet()
    {
        if (_live is null)
            return;
        _live.Load(Entries.Select(e => e.Path));
        Status = $"Sent {Entries.Count} tracks to the live set.";
    }

    private void RebuildEntriesFrom(StudioSet set)
    {
        Entries.Clear();
        SelectedEntry = null;
        foreach (StudioEntry entry in set.Entries)
        {
            MusicTrack? track = _byPath.GetValueOrDefault(entry.TrackPath);
            var transition = entry.TransitionIn is null ? null : new StudioTransitionViewModel(entry.TransitionIn);
            Entries.Add(new StudioEntryViewModel(entry.TrackPath, track, transition));
        }
        NormalizeTransitions();
        foreach (StudioEntryViewModel lane in Entries)
            LoadWaveform(lane);
    }

    // Enforce the set invariant after any structural change: the first lane has no incoming
    // transition; every later lane has one (a computed default when it doesn't already carry an
    // edited transition), so the timeline always shows a complete plan.
    private void NormalizeTransitions()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            StudioEntryViewModel lane = Entries[i];
            if (i == 0)
            {
                lane.TransitionIn = null;
                continue;
            }

            if (lane.TransitionIn is null)
            {
                MusicTrack? prev = Entries[i - 1].Track;
                MusicTrack? cur = lane.Track;
                StudioTransition model = prev is not null && cur is not null
                    ? TransitionDefaults.For(prev, cur)
                    : StudioTransition.Cut;
                lane.TransitionIn = new StudioTransitionViewModel(model);
            }
        }
    }

    // Fire-and-forget per-lane waveform decode (the provider degrades to Empty on failure). Optional:
    // with no provider wired (tests/headless) the lanes simply render without a waveform.
    private async void LoadWaveform(StudioEntryViewModel lane)
    {
        if (_waveforms is null)
            return;
        try
        {
            WaveformOverview overview = await Task.Run(
                () => _waveforms.GetOverviewAsync(lane.Path, WaveformBuckets)).ConfigureAwait(false);
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                lane.Peaks = overview.IsEmpty ? null : overview.Peaks;
                lane.KickPeaks = overview.IsEmpty ? null : overview.LowPeaks;
                lane.MidPeaks = overview.IsEmpty ? null : overview.MidPeaks;
                lane.HighPeaks = overview.IsEmpty ? null : overview.HighPeaks;
            });
        }
        catch (Exception)
        {
            // A single lane failing to decode must never break the planner — leave it waveform-less.
        }
    }

    private async Task RefreshSavedAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> names = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            SavedSets.Clear();
            foreach (string name in names)
                SavedSets.Add(name);
        });
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
