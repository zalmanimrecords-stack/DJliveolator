using System.Reactive.Concurrency;
using Liveolator.App.Features.Shared;
using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Shared;

public sealed class TrackContextActionsTests
{
    public TrackContextActionsTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        public RecordingDispatcher(int deckCount) => DeckCount = deckCount;
        public int DeckCount { get; }
        public List<PerformanceAction> Dispatched { get; } = new();
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => kind == PerformanceActionKind.DeckPlayPause && slot >= 0 && slot < DeckCount
                ? new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0)
                : ActionFeedbackState.Unavailable;
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged { add { } remove { } }
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
    }

    private sealed class FakePlaylistStore : IPlaylistStore
    {
        public Dictionary<string, Playlist> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Saved.Keys.OrderBy(k => k).ToList());
        public Task<Playlist?> LoadAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Saved.GetValueOrDefault(name));
        public Task SaveAsync(Playlist playlist, CancellationToken ct = default)
        {
            Saved[playlist.Name] = playlist;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string name, CancellationToken ct = default)
        {
            Saved.Remove(name);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void LoadToDeck_dispatches_DeckLoadTrack_with_slot_path_and_bpm()
    {
        var dispatcher = new RecordingDispatcher(deckCount: 2);
        var actions = new TrackContextActions(dispatcher, new FakePlaylistStore(),
            deckLoader: new Liveolator.Core.Playlist.DeckTrackLoader(dispatcher, _ => true));

        actions.LoadToDeck(1, "/m/a.wav", bpm: 126.0, firstBeatSeconds: 0.5);

        Assert.Equal(2, dispatcher.Dispatched.Count);
        PerformanceAction load = dispatcher.Dispatched[0];
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, load.Kind);
        Assert.Equal(1, load.Slot);
        Assert.Equal("/m/a.wav", load.Argument);
        Assert.Equal(126.0, load.Value, precision: 6); // BPM rides in Value → deck sync reference (doc 11)

        // The load is immediately followed by the downbeat anchor → phase-match (doc 22 A1).
        PerformanceAction anchor = dispatcher.Dispatched[1];
        Assert.Equal(PerformanceActionKind.DeckSetFirstBeat, anchor.Kind);
        Assert.Equal(1, anchor.Slot);
        Assert.Equal(0.5, anchor.Value, precision: 6);
    }

    [Fact]
    public void LoadToDeck_on_a_playing_deck_queues_the_track_instead()
    {
        var dispatcher = new PlayingDispatcher(playingSlot: 1);
        var actions = new TrackContextActions(dispatcher, new FakePlaylistStore(),
            deckLoader: new Liveolator.Core.Playlist.DeckTrackLoader(dispatcher, _ => true));

        actions.LoadToDeck(1, "/m/a.wav", bpm: 126.0);

        PerformanceAction append = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.PlaylistAppendTrack, append.Kind);
        Assert.Equal(1, append.Slot);
        Assert.Equal("/m/a.wav", append.Argument);
    }

    [Fact]
    public void LoadToDeck_with_an_unreachable_file_dispatches_nothing_and_reports()
    {
        var dispatcher = new RecordingDispatcher(deckCount: 2);
        string? status = null;
        var actions = new TrackContextActions(dispatcher, new FakePlaylistStore(),
            onStatus: s => status = s,
            deckLoader: new Liveolator.Core.Playlist.DeckTrackLoader(dispatcher, _ => false));

        actions.LoadToDeck(0, @"S:\offline\a.mp3", bpm: 126.0);

        Assert.Empty(dispatcher.Dispatched);
        Assert.NotNull(status);
        Assert.Contains("missing", status, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PlayingDispatcher : IPerformanceActionDispatcher
    {
        private readonly int _playingSlot;
        public PlayingDispatcher(int playingSlot) => _playingSlot = playingSlot;
        public List<PerformanceAction> Dispatched { get; } = new();
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => kind == PerformanceActionKind.DeckPlayPause
                ? new ActionFeedbackState(IsActive: slot == _playingSlot, IsAvailable: true, Value: 0)
                : ActionFeedbackState.Unavailable;
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged { add { } remove { } }
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    public void CanLoadToDeck_reflects_deck_count(int deckCount, bool a, bool b)
    {
        var actions = new TrackContextActions(new RecordingDispatcher(deckCount), new FakePlaylistStore());
        Assert.Equal(a, actions.CanLoadToDeckA);
        Assert.Equal(b, actions.CanLoadToDeckB);
    }

    [Fact]
    public void CanLoadToDeck_false_without_dispatcher()
    {
        var actions = new TrackContextActions(null, new FakePlaylistStore());
        Assert.False(actions.CanLoadToDeckA);
        Assert.False(actions.CanLoadToDeckB);
    }

    [Fact]
    public async Task AddToPlaylist_appends_and_dedupes_then_saves()
    {
        var store = new FakePlaylistStore();
        store.Saved["Peak"] = new Playlist("Peak", new[] { "/m/a.wav" });
        var actions = new TrackContextActions(null, store);

        await actions.AddToPlaylistAsync("/m/b.wav", "Peak");
        await actions.AddToPlaylistAsync("/m/b.wav", "Peak"); // duplicate → no-op

        Assert.Equal(new[] { "/m/a.wav", "/m/b.wav" }, store.Saved["Peak"].TrackPaths);
    }

    [Fact]
    public async Task AddToNewPlaylist_creates_a_uniquely_named_set()
    {
        var store = new FakePlaylistStore();
        store.Saved["New set"] = new Playlist("New set", Array.Empty<string>());
        var actions = new TrackContextActions(null, store);

        await actions.AddToNewPlaylistAsync("/m/a.wav");

        Assert.True(store.Saved.ContainsKey("New set (2)"));
        Assert.Equal(new[] { "/m/a.wav" }, store.Saved["New set (2)"].TrackPaths);
    }

    [Fact]
    public async Task RefreshPlaylists_populates_the_names()
    {
        var store = new FakePlaylistStore();
        store.Saved["Warmup"] = new Playlist("Warmup", Array.Empty<string>());
        store.Saved["Closing"] = new Playlist("Closing", Array.Empty<string>());
        var actions = new TrackContextActions(null, store);

        await actions.RefreshPlaylistsAsync();

        Assert.Equal(new[] { "Closing", "Warmup" }, actions.Playlists);
    }

    private sealed class FakeAutoCueService : Liveolator.Core.Analysis.Cues.IAutoCueService
    {
        private readonly int _cued;
        public FakeAutoCueService(int cued) => _cued = cued;
        public List<string> Requested { get; } = new();

        public Task<Liveolator.Core.Analysis.Cues.AutoCueOutcome> RunAsync(
            IReadOnlyList<string> trackPaths,
            IProgress<Liveolator.Core.Analysis.Cues.AutoCueProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requested.AddRange(trackPaths);
            return Task.FromResult(new Liveolator.Core.Analysis.Cues.AutoCueOutcome(trackPaths.Count, _cued));
        }
    }

    [Fact]
    public void CanAutoCue_reflects_whether_a_service_was_wired()
    {
        Assert.False(new TrackContextActions(null, new FakePlaylistStore()).CanAutoCue);
        Assert.True(new TrackContextActions(null, new FakePlaylistStore(),
            autoCueService: new FakeAutoCueService(cued: 1)).CanAutoCue);
    }

    [Fact]
    public async Task AutoCue_runs_the_service_for_the_track_and_reports_success()
    {
        var service = new FakeAutoCueService(cued: 1);
        string? status = null;
        var actions = new TrackContextActions(null, new FakePlaylistStore(),
            onStatus: s => status = s, autoCueService: service);

        await actions.AutoCueAsync(@"C:\m\a.wav");

        Assert.Equal(@"C:\m\a.wav", Assert.Single(service.Requested));
        Assert.NotNull(status);
        Assert.Contains("auto cues", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AutoCue_reports_when_no_cues_could_be_placed()
    {
        string? status = null;
        var actions = new TrackContextActions(null, new FakePlaylistStore(),
            onStatus: s => status = s, autoCueService: new FakeAutoCueService(cued: 0));

        await actions.AutoCueAsync(@"C:\m\a.wav");

        Assert.NotNull(status);
        Assert.Contains("No auto cues", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AutoCue_without_a_service_is_a_no_op()
    {
        var actions = new TrackContextActions(null, new FakePlaylistStore());
        await actions.AutoCueAsync(@"C:\m\a.wav"); // must not throw
    }
}
