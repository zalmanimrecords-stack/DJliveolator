using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using ReactiveUI;

namespace Liveolator.App.Features.Dj;

/// <summary>
/// DJ PRO harmonic-match widget (under deck A): "what fits after the track on deck A/B", from the shared
/// <see cref="TrackSuggestionRule"/> with the key / BPM / genre gates each switchable, closest BPM first.
/// Rows send a track to either deck through <see cref="DeckTrackLoader"/> — a free deck loads it, a
/// playing deck queues it. The list rebuilds on a deck load, a toggle, or tab entry — never on pitch
/// moves, so rows do not jump under the pointer mid-set.
/// </summary>
public sealed class HarmonicMatchesViewModel : ViewModelBase, IDisposable
{
    private static readonly TrackSuggestionRule Rule = new();

    private readonly MusicLibrary _library;
    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly DeckTrackLoader _loader;
    private readonly IDisposable _toggles;

    private bool _isSourceB;
    private bool _matchKey = true;
    private bool _matchBpm = true;
    private bool _matchGenre = true;
    private bool _isExpanded = true;
    private string _summary = string.Empty;
    private string _loadStatus = string.Empty;

    public HarmonicMatchesViewModel(
        MusicLibrary library, IPerformanceActionDispatcher dispatcher, DeckTrackLoader? loader = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _loader = loader ?? new DeckTrackLoader(dispatcher, System.IO.File.Exists);

        LoadRowToDeckACommand = ReactiveCommand.Create<TrackRowViewModel>(row => Send(row, 0));
        LoadRowToDeckBCommand = ReactiveCommand.Create<TrackRowViewModel>(row => Send(row, 1));
        ToggleExpandedCommand = ReactiveCommand.Create(() => { IsExpanded = !IsExpanded; });

        _toggles = this.WhenAnyValue(x => x.IsSourceB, x => x.MatchKey, x => x.MatchBpm, x => x.MatchGenre)
            .Subscribe(_ => Refresh());
        _dispatcher.FeedbackChanged += OnFeedback;
    }

    public ObservableCollection<TrackRowViewModel> Matches { get; } = new();

    /// <summary>True = match against deck B's track; false = deck A's.</summary>
    public bool IsSourceB
    {
        get => _isSourceB;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSourceB, value);
            this.RaisePropertyChanged(nameof(IsSourceA));
        }
    }

    /// <summary>Radio-button mirror of <see cref="IsSourceB"/>; only a check (true) acts.</summary>
    public bool IsSourceA
    {
        get => !_isSourceB;
        set { if (value) IsSourceB = false; }
    }

    public bool MatchKey
    {
        get => _matchKey;
        set => this.RaiseAndSetIfChanged(ref _matchKey, value);
    }

    public bool MatchBpm
    {
        get => _matchBpm;
        set => this.RaiseAndSetIfChanged(ref _matchBpm, value);
    }

    public bool MatchGenre
    {
        get => _matchGenre;
        set => this.RaiseAndSetIfChanged(ref _matchGenre, value);
    }

    /// <summary>Collapsed = header only, so laptop tiers can hand the height back to the deck.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    /// <summary>What the list is matched against, or why it is empty.</summary>
    public string Summary
    {
        get => _summary;
        private set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    /// <summary>Outcome of the last send ("Loaded on A", "Queued on B", or why it failed).</summary>
    public string LoadStatus
    {
        get => _loadStatus;
        private set => this.RaiseAndSetIfChanged(ref _loadStatus, value);
    }

    public ReactiveCommand<TrackRowViewModel, Unit> LoadRowToDeckACommand { get; }
    public ReactiveCommand<TrackRowViewModel, Unit> LoadRowToDeckBCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleExpandedCommand { get; }

    /// <summary>Rebuilds from the current catalog snapshot and the decks' loaded tracks.</summary>
    public void Refresh()
    {
        int slot = IsSourceB ? 1 : 0;
        char deck = IsSourceB ? 'B' : 'A';
        Matches.Clear();

        string? seedPath = LoadedPath(slot);
        if (seedPath is null)
        {
            Summary = $"No track on deck {deck}";
            return;
        }

        MusicTrack? seed = _library.TryGetByPathOrName(seedPath);
        if (seed is null)
        {
            Summary = $"Deck {deck} track is not in the catalog";
            return;
        }

        var options = new TrackSuggestionOptions(
            Genre: MatchGenre ? GenreMatch.Strict : GenreMatch.Off, MatchKey: MatchKey, MatchBpm: MatchBpm);
        string? otherDeck = LoadedPath(1 - slot);
        foreach (TrackSuggestion match in Rule.Suggest(
                     seed, _library.All, options, otherDeck is null ? null : new[] { otherDeck }))
            Matches.Add(new TrackRowViewModel(match.Track));

        // An untagged or keyless seed opens that gate (the rule's choice) — say so, or the list looks wrong.
        string key = seed.Key?.Camelot ?? "key: any";
        string bpm = seed.Bpm is { } b ? $"{b.Bpm:0.0} BPM" : "BPM: any";
        string genre = !MatchGenre ? "genre off" : seed.Metadata?.Genre is { Length: > 0 } g ? g : "genre: any";
        Summary = $"{deck} · {key} · {bpm} · {genre}" + (Matches.Count == 0 ? " — no matches" : string.Empty);
    }

    public void Dispose()
    {
        _dispatcher.FeedbackChanged -= OnFeedback;
        _toggles.Dispose();
    }

    // Any deck load changes either the seed or the other-deck exclusion. Feedback can arrive off the UI thread.
    private void OnFeedback(object? sender, ActionFeedbackChanged e)
    {
        if (e.Kind == PerformanceActionKind.DeckLoadTrack)
            RxApp.MainThreadScheduler.Schedule(Refresh);
    }

    private string? LoadedPath(int slot)
        => _dispatcher.GetFeedback(PerformanceActionKind.DeckLoadTrack, slot)
            is { IsAvailable: true, Argument: { Length: > 0 } path } ? path : null;

    private void Send(TrackRowViewModel? row, int slot)
    {
        if (row is not null)
            LoadStatus = _loader.Load(slot, row.Track.File.Path, row.Track.Bpm).Message;
    }
}
