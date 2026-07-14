using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Features.Shared;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using ReactiveUI;

namespace Liveolator.App.Features.Dj;

/// <summary>
/// The DJ-tab track browser: a focused, performance-oriented view over the SAME catalog the LIBRARIES tab
/// manages (the shared <see cref="MusicLibrary"/>), with its own independent search/sort/selection state.
/// It is deliberately NOT a second library manager — there is no scan/import/auto-cue/folder surface here
/// (those CPU-heavy setup actions must never be one click from the performance surface). It only:
/// searches (text), sorts (BPM/Key/Title/Time), and loads a track onto a deck through the shared
/// <see cref="DeckTrackLoader"/> (reachability-checked, load-or-queue, never cuts a playing deck).
/// Reuses the Core query/sort (<see cref="TrackQuery"/>/<see cref="TrackSort"/>) and the
/// <see cref="TrackRowViewModel"/> presentation — no duplicated catalog logic.
/// </summary>
public sealed class DjBrowserViewModel : ViewModelBase
{
    private readonly MusicLibrary _library;
    private readonly IPerformanceActionDispatcher? _dispatcher;
    private readonly DeckTrackLoader? _loader;
    private readonly TrackContextActions? _contextActions;
    private readonly PerformanceDeckSet? _decks;

    // Row view-models cached per track so search/sort just reorder the same instances (no churn, no
    // dropped selection) — mirrors the LIBRARIES tab's rowByTrack approach.
    private readonly Dictionary<MusicTrack, TrackRowViewModel> _rowByTrack = new();

    private string _searchText = string.Empty;
    private TrackSortKey _sortKey = TrackSortKey.Bpm;
    private bool _sortDescending;
    private TrackRowViewModel? _selectedTrack;
    private string _loadStatus = string.Empty;

    public DjBrowserViewModel(
        MusicLibrary library,
        IPerformanceActionDispatcher? dispatcher = null,
        DeckTrackLoader? loader = null,
        TrackContextActions? contextActions = null,
        PerformanceDeckSet? decks = null)
    {
        _library = library ?? throw new System.ArgumentNullException(nameof(library));
        _dispatcher = dispatcher;
        _contextActions = contextActions;
        _decks = decks;
        // Same load-or-queue policy as the LIBRARIES tab; tests inject a fake loader.
        _loader = loader
            ?? (dispatcher is null ? null : new DeckTrackLoader(dispatcher, System.IO.File.Exists));

        CanLoadToDeckA = DeckSlotAvailable(0);
        CanLoadToDeckB = DeckSlotAvailable(1);

        LoadToDeckACommand = ReactiveCommand.Create(
            () => LoadToDeck(SelectedTrack, 0),
            this.WhenAnyValue(x => x.SelectedTrack).Select(t => t is not null && CanLoadToDeckA));
        LoadToDeckBCommand = ReactiveCommand.Create(
            () => LoadToDeck(SelectedTrack, 1),
            this.WhenAnyValue(x => x.SelectedTrack).Select(t => t is not null && CanLoadToDeckB));

        // Per-row load: a row's "-> A" / "-> B" button loads THAT row (no need to select it first), the
        // unambiguous pro pattern. Gated only on the deck slot existing.
        LoadRowToDeckACommand = ReactiveCommand.Create<TrackRowViewModel>(
            row => LoadToDeck(row, 0), Observable.Return(CanLoadToDeckA));
        LoadRowToDeckBCommand = ReactiveCommand.Create<TrackRowViewModel>(
            row => LoadToDeck(row, 1), Observable.Return(CanLoadToDeckB));

        // Header sort taps: the two a DJ sorts by when digging for the next track. Tapping the active
        // key flips direction.
        SortByBpmCommand = ReactiveCommand.Create(() => ToggleSort(TrackSortKey.Bpm));
        SortByKeyCommand = ReactiveCommand.Create(() => ToggleSort(TrackSortKey.Key));

        Observable.Merge(
                this.WhenAnyValue(x => x.SearchText).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SortKey).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SortDescending).Select(_ => Unit.Default))
            .Subscribe(_ => ApplyFilter());

        Refresh();
    }

    /// <summary>The visible rows after search + sort (virtualized by the view).</summary>
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    public TrackSortKey SortKey
    {
        get => _sortKey;
        set => this.RaiseAndSetIfChanged(ref _sortKey, value);
    }

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

    /// <summary>The outcome of the last load attempt (e.g. "Loaded on A", "Queued on B", or why it
    /// couldn't load) — surfaced so a queue onto a playing deck is never silent on the floor.</summary>
    public string LoadStatus
    {
        get => _loadStatus;
        private set => this.RaiseAndSetIfChanged(ref _loadStatus, value);
    }

    public bool CanLoadToDeckA { get; }
    public bool CanLoadToDeckB { get; }

    public ReactiveCommand<Unit, Unit> LoadToDeckACommand { get; }
    public ReactiveCommand<Unit, Unit> LoadToDeckBCommand { get; }
    public ReactiveCommand<TrackRowViewModel, Unit> LoadRowToDeckACommand { get; }
    public ReactiveCommand<TrackRowViewModel, Unit> LoadRowToDeckBCommand { get; }
    public ReactiveCommand<Unit, Unit> SortByBpmCommand { get; }
    public ReactiveCommand<Unit, Unit> SortByKeyCommand { get; }

    /// <summary>Re-reads the shared catalog (the snapshot <see cref="MusicLibrary.All"/>) and re-applies the
    /// current search/sort. Called on construction and whenever the DJ tab is (re)entered, so tracks scanned
    /// in the LIBRARIES tab appear here without a second scan or a catalog-change event.</summary>
    public void Refresh()
    {
        foreach (MusicTrack track in _library.All)
            if (!_rowByTrack.ContainsKey(track))
                _rowByTrack[track] = new TrackRowViewModel(track, _contextActions);

        // Drop rows whose track left the catalog (e.g. a folder was pruned).
        var present = new HashSet<MusicTrack>(_library.All);
        foreach (MusicTrack gone in new List<MusicTrack>(_rowByTrack.Keys))
            if (!present.Contains(gone))
                _rowByTrack.Remove(gone);

        ApplyFilter();
    }

    /// <summary>Double-click load: only acts when the choice is unambiguous — exactly one deck playing, so
    /// the OTHER (free) deck takes the track. Both stopped (pre-show) or both playing → do nothing and let
    /// the explicit A/B buttons decide, so a wrong-deck load can never happen by accident mid-set.</summary>
    public void LoadToFreeDeck()
    {
        if (_decks is null)
            return;
        int? slot = FreeDeckSlot(_decks.DeckA.IsPlaying, _decks.DeckB.IsPlaying);
        if (slot is { } s)
            LoadToDeck(SelectedTrack, s);
    }

    /// <summary>The unambiguous "free deck" for a double-click load: the not-playing deck when exactly one
    /// deck is playing; otherwise none. Pure so the rule is unit-testable.</summary>
    public static int? FreeDeckSlot(bool deckAPlaying, bool deckBPlaying)
    {
        if (deckAPlaying && !deckBPlaying) return 1;
        if (deckBPlaying && !deckAPlaying) return 0;
        return null;
    }

    private void ToggleSort(TrackSortKey key)
    {
        if (SortKey == key)
            SortDescending = !SortDescending;
        else
            SortKey = key;
    }

    private void ApplyFilter()
    {
        var filter = new TrackFilter(Text: SearchText);
        IReadOnlyList<MusicTrack> filtered = TrackQuery.Apply(_rowByTrack.Keys, filter, TrackQuery.MaxResults);
        IReadOnlyList<MusicTrack> ordered = TrackSort.Apply(filtered, SortKey, SortDescending);

        Tracks.Clear();
        foreach (MusicTrack track in ordered)
            Tracks.Add(_rowByTrack[track]);
    }

    // Stage a track on a deck slot via the shared load-or-queue policy — no auto-play (load ≠ play).
    // A playing deck queues it; an unreachable file dispatches nothing and reports why.
    private void LoadToDeck(TrackRowViewModel? track, int slot)
    {
        if (_loader is null || track is null)
            return;

        LoadStatus = _loader.Load(
            slot,
            track.Track.File.Path,
            bpm: track.Track.Bpm?.Bpm ?? 0,
            firstBeatSeconds: track.Track.Bpm?.FirstBeatSeconds ?? 0).Message;
    }

    // A deck slot is loadable only if the engine backs it (DeckPlayPause reports IsAvailable iff
    // slot < engine.DeckCount) — the same feedback-seam check the LIBRARIES tab uses. Null dispatcher ⇒ no decks.
    private bool DeckSlotAvailable(int slot)
        => _dispatcher?.GetFeedback(PerformanceActionKind.DeckPlayPause, slot).IsAvailable ?? false;
}
