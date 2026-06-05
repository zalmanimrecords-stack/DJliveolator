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
    public void LoadToDeck_dispatches_DeckLoadTrack_with_slot_and_path()
    {
        var dispatcher = new RecordingDispatcher(deckCount: 2);
        var actions = new TrackContextActions(dispatcher, new FakePlaylistStore());

        actions.LoadToDeck(1, "/m/a.wav");

        PerformanceAction a = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, a.Kind);
        Assert.Equal(1, a.Slot);
        Assert.Equal("/m/a.wav", a.Argument);
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
}
