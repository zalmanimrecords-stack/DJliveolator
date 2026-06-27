using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using Liveolator.Core.Waveform;
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
    private readonly ILivePlaylist? _deckBQueue;
    private readonly MusicLibrary? _library;
    private readonly Shared.TrackContextActions? _contextActions;
    private readonly PerformanceDeckSet _decks;
    private readonly bool _ownsDecks;
    private bool _disposed;
    // Played-history tracking (B5): the entry currently in Now, and the id we expect to become Now on
    // the next advance (the prior Next). They let us tell an advance (record the leaving track) from a
    // fresh Load (reset history) without changing the queue engine.
    private QueueEntry? _previousNow;
    private Guid? _expectedNextId;

    /// <param name="decks">The shared decks + crossfader (doc 11). When provided, the DJ tab drives the
    /// same instances as the Live tab (one source of truth); when null it builds a private set so the
    /// view-model still constructs headless / under test.</param>
    public DjViewModel(
        IPerformanceActionDispatcher? dispatcher = null,
        ILivePlaylist? playlist = null,
        MusicLibrary? library = null,
        Shared.TrackContextActions? contextActions = null,
        IWaveformProvider? waveformProvider = null,
        PerformanceDeckSet? decks = null,
        ILivePlaylist? deckBQueue = null)
    {
        _dispatcher = dispatcher;
        _playlist = playlist;
        _library = library;
        _contextActions = contextActions;
        _deckBQueue = deckBQueue;

        _ownsDecks = decks is null;
        _decks = decks ?? new PerformanceDeckSet(dispatcher, waveformProvider, library);
        // DJ-tab track browser (the Rekordbox-style bottom half): a focused view over the SAME catalog,
        // independent of the LIBRARIES tab, with no scan/import surface. Only when a catalog is wired.
        Browser = library is null
            ? null
            : new DjBrowserViewModel(library, dispatcher, loader: null, contextActions, _decks);
        Set = new ObservableCollection<SetEntryViewModel>();
        Played = new ObservableCollection<SetEntryViewModel>();
        DeckBQueue = new ObservableCollection<SetEntryViewModel>();

        IObservable<bool> canEdit = Observable.Return(dispatcher is not null && playlist is not null);
        SkipCommand = ReactiveCommand.Create(SkipToNext, canEdit);
        LoadFromLibraryCommand = ReactiveCommand.Create(
            LoadFromLibrary, Observable.Return(playlist is not null && library is not null));

        if (_playlist is not null)
        {
            _playlist.NowChanged += OnNowChanged;
            RefreshSet();
            CaptureQueuePosition(); // seed the played-history tracking from the initial queue
        }

        if (_deckBQueue is not null)
        {
            _deckBQueue.Changed += OnDeckBQueueChanged;
            RefreshDeckBQueue();
        }
    }

    public DeckViewModel DeckA => _decks.DeckA;
    public DeckViewModel DeckB => _decks.DeckB;
    public MixerViewModel Mixer => _decks.Mixer;

    /// <summary>The shared deck set — exposes the waveform ZOOM knob (<see cref="PerformanceDeckSet.WaveformZoom"/>).</summary>
    public PerformanceDeckSet Decks => _decks;

    /// <summary>The DJ-tab track browser (bottom half), or null when no catalog is wired (headless/tests).</summary>
    public DjBrowserViewModel? Browser { get; }

    /// <summary>True when a browser is available to show in the DJ console.</summary>
    public bool HasBrowser => Browser is not null;

    /// <summary>The set: the Now entry first, then the upcoming queue, in play order.</summary>
    public ObservableCollection<SetEntryViewModel> Set { get; }

    /// <summary>Already-played tracks, most-recent first (B5). Read-only history, fed as the queue advances.</summary>
    public ObservableCollection<SetEntryViewModel> Played { get; }

    /// <summary>True when nothing has been played yet (the view hides the Played section).</summary>
    public bool IsPlayedEmpty => Played.Count == 0;

    /// <summary>
    /// Deck B's own queue (the playing entry first, then the upcoming order): tracks loaded onto a
    /// playing deck B land here and play when the current track ends (doc 09/11).
    /// </summary>
    public ObservableCollection<SetEntryViewModel> DeckBQueue { get; }

    /// <summary>True when deck B's queue is empty (the view hides its section).</summary>
    public bool IsDeckBQueueEmpty => DeckBQueue.Count == 0;

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
        if (_deckBQueue is not null)
            _deckBQueue.Changed -= OnDeckBQueueChanged;
        // Only dispose the decks this view-model created; a shared set is owned by the composition root.
        if (_ownsDecks)
            _decks.Dispose();
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
        => RxApp.MainThreadScheduler.Schedule(() =>
        {
            UpdatePlayedHistory(now);
            RefreshSet();
        });

    // Decides whether Now changed because the queue advanced (the prior Next became Now → the track
    // that left Now is "played") or because a fresh set was loaded (→ reset history). The expected-next
    // id captured on the previous change is the deterministic signal; the queue engine is untouched.
    private void UpdatePlayedHistory(QueueEntry? now)
    {
        bool advanced = _previousNow is not null && now?.Id == _expectedNextId;
        if (advanced)
        {
            RecordPlayed(_previousNow!);
        }
        else if (_previousNow is not null || now is not null)
        {
            // A reload/replace (not a sequential advance): the prior history no longer applies.
            Played.Clear();
            this.RaisePropertyChanged(nameof(IsPlayedEmpty));
        }

        CaptureQueuePosition();
    }

    private void RecordPlayed(QueueEntry entry)
    {
        var played = new QueueEntry(entry.TrackPath, entry.Id, TrackState.Played);
        // No remove callback ⇒ history is read-only; titles come from the same catalog lookup as the set.
        Played.Insert(0, new SetEntryViewModel(played, TitleFor(entry.TrackPath), null, _contextActions));
        this.RaisePropertyChanged(nameof(IsPlayedEmpty));
    }

    // Snapshots the current Now and the id of the next-up entry, so the following NowChanged can tell
    // an advance from a reload.
    private void CaptureQueuePosition()
    {
        _previousNow = _playlist?.Now;
        _expectedNextId = _playlist is { Upcoming: { Count: > 0 } up } ? up[0].Id : null;
    }

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

    private void OnDeckBQueueChanged(object? sender, EventArgs e)
        => RxApp.MainThreadScheduler.Schedule(RefreshDeckBQueue);

    // Mirrors RefreshSet for deck B's queue. Future entries are removable through the dispatcher,
    // addressed to deck B's queue via Slot = 1; the playing entry is protected (null callback).
    private void RefreshDeckBQueue()
    {
        DeckBQueue.Clear();
        if (_deckBQueue is not null)
        {
            IReadOnlyDictionary<string, string> titles = BuildTitleLookup();
            if (_deckBQueue.Now is { } now)
                DeckBQueue.Add(MakeDeckBEntry(now, titles));
            foreach (QueueEntry entry in _deckBQueue.Upcoming)
                DeckBQueue.Add(MakeDeckBEntry(entry, titles));
        }
        this.RaisePropertyChanged(nameof(IsDeckBQueueEmpty));
    }

    private SetEntryViewModel MakeDeckBEntry(QueueEntry entry, IReadOnlyDictionary<string, string> titles)
    {
        string title = titles.TryGetValue(entry.TrackPath, out string? known)
            ? known
            : Path.GetFileNameWithoutExtension(entry.TrackPath);

        Action? remove = _dispatcher is not null && entry.State != TrackState.Now
            ? () =>
            {
                _dispatcher.Dispatch(new PerformanceAction(
                    PerformanceActionKind.PlaylistRemoveFutureTrack, Slot: 1, Argument: entry.Id.ToString()));
                RefreshDeckBQueue();
            }
        : null;

        return new SetEntryViewModel(entry, title, remove, _contextActions);
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

        return new SetEntryViewModel(entry, title, remove, _contextActions);
    }

    // Title from the catalog for a single path (history is recorded one entry at a time), or the file
    // name when the track isn't catalogued.
    private string TitleFor(string trackPath)
    {
        if (_library is not null)
            foreach (MusicTrack track in _library.All)
                if (string.Equals(track.File.Path, trackPath, StringComparison.OrdinalIgnoreCase))
                    return track.Title;
        return Path.GetFileNameWithoutExtension(trackPath);
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
