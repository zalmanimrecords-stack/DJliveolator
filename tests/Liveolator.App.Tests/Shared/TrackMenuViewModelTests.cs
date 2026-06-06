using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Shared;
using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Shared;

public sealed class TrackMenuViewModelTests
{
    public TrackMenuViewModelTests()
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
        public Task SaveAsync(Playlist playlist, CancellationToken ct = default) { Saved[playlist.Name] = playlist; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct = default) { Saved.Remove(name); return Task.CompletedTask; }
    }

    [Fact]
    public async Task LoadToDeckB_delegates_to_actions_with_the_row_path()
    {
        var dispatcher = new RecordingDispatcher(deckCount: 2);
        var actions = new TrackContextActions(dispatcher, new FakePlaylistStore());
        var menu = new TrackMenuViewModel("/m/track.wav", actions);

        await menu.LoadToDeckBCommand.Execute().ToTask();

        PerformanceAction a = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, a.Kind);
        Assert.Equal(1, a.Slot);
        Assert.Equal("/m/track.wav", a.Argument);
    }

    [Fact]
    public async Task LoadToDeck_forwards_the_row_bpm_as_the_sync_reference()
    {
        var dispatcher = new RecordingDispatcher(deckCount: 2);
        var actions = new TrackContextActions(dispatcher, new FakePlaylistStore());
        var menu = new TrackMenuViewModel("/m/track.wav", actions, bpm: 124.0);

        await menu.LoadToDeckACommand.Execute().ToTask();

        PerformanceAction a = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(124.0, a.Value, precision: 6);
    }

    [Fact]
    public void CanLoadToDeckB_reflects_actions()
    {
        var oneDeck = new TrackMenuViewModel("/m/a.wav", new TrackContextActions(new RecordingDispatcher(1), new FakePlaylistStore()));
        var twoDeck = new TrackMenuViewModel("/m/a.wav", new TrackContextActions(new RecordingDispatcher(2), new FakePlaylistStore()));

        Assert.False(oneDeck.CanLoadToDeckB);
        Assert.True(twoDeck.CanLoadToDeckB);
    }

    [Fact]
    public async Task AddToPlaylistItems_lists_saved_sets_plus_a_new_set_item()
    {
        var store = new FakePlaylistStore();
        store.Saved["Closing"] = new Playlist("Closing", Array.Empty<string>());
        store.Saved["Warmup"] = new Playlist("Warmup", Array.Empty<string>());
        var actions = new TrackContextActions(null, store);
        await actions.RefreshPlaylistsAsync();
        var menu = new TrackMenuViewModel("/m/a.wav", actions);

        IReadOnlyList<MenuActionViewModel> items = menu.AddToPlaylistItems;

        Assert.Equal(3, items.Count); // 2 saved + "New set…"
        Assert.Equal("Closing", items[0].Header);
        Assert.Equal("Warmup", items[1].Header);
        Assert.Contains("New set", items[2].Header);
    }

    [Fact]
    public async Task AddToPlaylistItem_appends_the_row_track_to_the_chosen_set()
    {
        var store = new FakePlaylistStore();
        store.Saved["Peak"] = new Playlist("Peak", Array.Empty<string>());
        var actions = new TrackContextActions(null, store);
        await actions.RefreshPlaylistsAsync();
        var menu = new TrackMenuViewModel("/m/a.wav", actions);

        MenuActionViewModel peakItem = menu.AddToPlaylistItems.Single(i => i.Header == "Peak");
        peakItem.Command.Execute(null);

        Assert.Equal(new[] { "/m/a.wav" }, store.Saved["Peak"].TrackPaths);
    }
}
