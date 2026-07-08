using Liveolator.App.Composition;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.App.Tests.Composition;

public sealed class DeckSessionPersistenceTests
{
    [Fact]
    public void Constructor_RestoresExistingTracksThroughDeckActions()
    {
        string track = System.IO.Path.GetTempFileName();
        try
        {
            var dispatcher = new FakeDispatcher();
            var store = new FakeDeckSessionStore(
                [new DeckSessionState(1, track, 128, 0.2)]);

            using var persistence = new DeckSessionPersistence(dispatcher, store, deckCount: 2);

            Assert.Collection(
                dispatcher.Dispatched,
                load =>
                {
                    Assert.Equal(PerformanceActionKind.DeckLoadTrack, load.Kind);
                    Assert.Equal(1, load.Slot);
                    Assert.Equal(track, load.Argument);
                    Assert.Equal(128, load.Value);
                },
                firstBeat =>
                {
                    Assert.Equal(PerformanceActionKind.DeckSetFirstBeat, firstBeat.Kind);
                    Assert.Equal(1, firstBeat.Slot);
                    Assert.Equal(0.2, firstBeat.Value);
                });
        }
        finally
        {
            File.Delete(track);
        }
    }

    [Fact]
    public async Task LoadFeedback_AutosavesLatestDeckSnapshot()
    {
        var dispatcher = new FakeDispatcher();
        var store = new FakeDeckSessionStore();
        using var persistence = new DeckSessionPersistence(dispatcher, store, deckCount: 2);

        dispatcher.RaiseFeedback(
            PerformanceActionKind.DeckLoadTrack,
            0,
            new ActionFeedbackState(true, true, 126, "/m/a.wav"));

        await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        DeckSessionState saved = Assert.Single(store.LastSaved!);
        Assert.Equal(new DeckSessionState(0, "/m/a.wav", 126, 0), saved);
    }

    [Fact]
    public void Constructor_ReAppliesAManuallySetDownbeat_WhenSaved()
    {
        string track = System.IO.Path.GetTempFileName();
        try
        {
            var dispatcher = new FakeDispatcher();
            var store = new FakeDeckSessionStore(
                [new DeckSessionState(1, track, 128, 0.2, DownbeatSeconds: 0.55)]);

            using var persistence = new DeckSessionPersistence(dispatcher, store, deckCount: 2);

            // The saved "one" rides back through its own action so the deck re-anchors its bars on restart.
            Assert.Contains(dispatcher.Dispatched, a =>
                a.Kind == PerformanceActionKind.DeckSetDownbeat && a.Slot == 1 && Math.Abs(a.Value - 0.55) < 1e-9);
        }
        finally
        {
            File.Delete(track);
        }
    }

    [Fact]
    public async Task AnalysisOriginDownbeat_IsNotPersisted_ButALaterManualOneIs()
    {
        var dispatcher = new FakeDispatcher();
        var store = new FakeDeckSessionStore([new DeckSessionState(0, "/m/a.wav", 126, 0.1)]);
        using var persistence = new DeckSessionPersistence(
            dispatcher, store, deckCount: 2, enableRetryTimer: false);

        // The deck auto-derived a downbeat from track analysis: the tagged action, then its engine echo.
        dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetDownbeat, ActionInputMode.Absolute,
            Value: 0.7, Slot: 0, Origin: Liveolator.App.Features.Live.Modules.DeckViewModel.AnalysisOrigin));
        dispatcher.RaiseFeedback(
            PerformanceActionKind.DeckSetDownbeat, 0, new ActionFeedbackState(false, true, 0.7));

        // The analyzer's guess is NOT a manual edit — nothing may be saved for it.
        Assert.False(store.Saved.Task.IsCompleted);

        // A real SET ONE afterwards (no origin = a human gesture) still persists.
        dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckSetDownbeat, ActionInputMode.Absolute, Value: 0.9, Slot: 0));
        dispatcher.RaiseFeedback(
            PerformanceActionKind.DeckSetDownbeat, 0, new ActionFeedbackState(false, true, 0.9));

        await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        DeckSessionState saved = Assert.Single(store.LastSaved!);
        Assert.Equal(0.9, saved.DownbeatSeconds, 6);
    }

    [Fact]
    public async Task DownbeatFeedback_PersistsTheManuallySetOne()
    {
        var dispatcher = new FakeDispatcher();
        // Restore seeds the deck entry (the file need not exist — deferred entries are still tracked); the
        // retry timer is off so the test is deterministic.
        var store = new FakeDeckSessionStore([new DeckSessionState(0, "/m/a.wav", 126, 0.1)]);
        using var persistence = new DeckSessionPersistence(
            dispatcher, store, deckCount: 2, enableRetryTimer: false);

        dispatcher.RaiseFeedback(
            PerformanceActionKind.DeckSetDownbeat, 0, new ActionFeedbackState(false, true, 0.55));

        await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        DeckSessionState saved = Assert.Single(store.LastSaved!);
        Assert.Equal(0.55, saved.DownbeatSeconds, 6);
    }

    private sealed class FakeDeckSessionStore : IDeckSessionStore
    {
        private readonly IReadOnlyList<DeckSessionState>? _loaded;

        public FakeDeckSessionStore(IReadOnlyList<DeckSessionState>? loaded = null)
            => _loaded = loaded;

        public TaskCompletionSource Saved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<DeckSessionState>? LastSaved { get; private set; }

        public Task<IReadOnlyList<DeckSessionState>?> LoadAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(_loaded);

        public Task SaveAsync(
            IReadOnlyList<DeckSessionState> decks,
            CancellationToken cancellationToken = default)
        {
            LastSaved = decks;
            Saved.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
