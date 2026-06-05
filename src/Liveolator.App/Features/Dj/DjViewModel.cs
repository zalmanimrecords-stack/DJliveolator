using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using ReactiveUI;

namespace Liveolator.App.Features.Dj;

/// <summary>
/// The DJ tab — focused on music playback only: the two decks plus the live "set" (the Now/Next/Later
/// queue that is going to play, doc 09). Decks and the crossfader are the shared performance modules
/// (<see cref="DeckViewModel"/>, <see cref="MixerViewModel"/>) driven through the one dispatcher (doc 04),
/// so playing/mixing here is identical to the Live tab. The set is read from <see cref="ILivePlaylist"/>
/// (like the beat readout reads the clock) and edited through the dispatcher's playlist actions.
/// </summary>
public sealed class DjViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly ILivePlaylist? _playlist;
    private readonly MusicLibrary? _library;
    private bool _disposed;

    public DjViewModel(
        IPerformanceActionDispatcher? dispatcher = null,
        ILivePlaylist? playlist = null,
        MusicLibrary? library = null)
    {
        _dispatcher = dispatcher;
        _playlist = playlist;
        _library = library;

        DeckA = new DeckViewModel(slot: 0, dispatcher);
        DeckB = new DeckViewModel(slot: 1, dispatcher);
        Mixer = new MixerViewModel(dispatcher);
        Set = new ObservableCollection<SetEntryViewModel>();

        IObservable<bool> canEdit = Observable.Return(dispatcher is not null && playlist is not null);
        SkipCommand = ReactiveCommand.Create(SkipToNext, canEdit);
        LoadFromLibraryCommand = ReactiveCommand.Create(
            LoadFromLibrary, Observable.Return(playlist is not null && library is not null));

        if (_playlist is not null)
        {
            _playlist.NowChanged += OnNowChanged;
            RefreshSet();
        }
    }

    public DeckViewModel DeckA { get; }
    public DeckViewModel DeckB { get; }
    public MixerViewModel Mixer { get; }

    /// <summary>The set: the Now entry first, then the upcoming queue, in play order.</summary>
    public ObservableCollection<SetEntryViewModel> Set { get; }

    public ReactiveCommand<Unit, Unit> SkipCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadFromLibraryCommand { get; }

    /// <summary>True when the set/queue is wired (drives the set controls' enabled state).</summary>
    public bool IsEnabled => _dispatcher is not null && _playlist is not null;

    /// <summary>True when a catalog is available to fill the set from.</summary>
    public bool HasLibrary => _library is not null;

    /// <summary>True when the set has no tracks (the view shows a hint to load the library / scan first).</summary>
    public bool IsSetEmpty => Set.Count == 0;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_playlist is not null)
            _playlist.NowChanged -= OnNowChanged;
        DeckA.Dispose();
        DeckB.Dispose();
        Mixer.Dispose();
    }

    // Bulk set construction has no single-action representation (a PerformanceAction carries one track,
    // not a list, and Load sets the first track as Now). It is a setup operation, not a live performance
    // edit, so it calls the queue model directly; the live edits below (skip/remove) go through the
    // dispatcher (doc 04).
    private void LoadFromLibrary()
    {
        if (_playlist is null || _library is null)
            return;
        _playlist.Load(_library.All.Select(track => track.File.Path));
        RefreshSet();
    }

    private void SkipToNext()
    {
        _dispatcher?.Dispatch(new PerformanceAction(PerformanceActionKind.PlaylistSkipOnNextBar));
        RefreshSet();
    }

    private void OnNowChanged(object? sender, QueueEntry? now)
        => RxApp.MainThreadScheduler.Schedule(RefreshSet);

    private void RefreshSet()
    {
        Set.Clear();
        if (_playlist is not null)
        {
            IReadOnlyDictionary<string, string> titles = BuildTitleLookup();
            if (_playlist.Now is { } now)
                Set.Add(MakeEntry(now, titles));
            foreach (QueueEntry entry in _playlist.Upcoming)
                Set.Add(MakeEntry(entry, titles));
        }
        this.RaisePropertyChanged(nameof(IsSetEmpty));
    }

    private SetEntryViewModel MakeEntry(QueueEntry entry, IReadOnlyDictionary<string, string> titles)
    {
        string title = titles.TryGetValue(entry.TrackPath, out string? known)
            ? known
            : Path.GetFileNameWithoutExtension(entry.TrackPath);

        // Future entries can be removed through the dispatcher; Now is protected (null callback).
        Action? remove = _dispatcher is not null && entry.State != TrackState.Now
            ? () =>
            {
                _dispatcher.Dispatch(new PerformanceAction(
                    PerformanceActionKind.PlaylistRemoveFutureTrack, Argument: entry.Id.ToString()));
                RefreshSet();
            }
        : null;

        return new SetEntryViewModel(entry, title, remove);
    }

    private Dictionary<string, string> BuildTitleLookup()
    {
        var titles = new Dictionary<string, string>();
        if (_library is not null)
            foreach (MusicTrack track in _library.All)
                titles[track.File.Path] = track.Title; // last write wins on duplicate paths
        return titles;
    }
}
