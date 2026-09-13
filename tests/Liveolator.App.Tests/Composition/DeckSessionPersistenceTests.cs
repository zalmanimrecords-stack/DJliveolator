using Liveolator.App.Composition;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.App.Tests.Composition;

public sealed class DeckSessionPersistenceTests
{
    [Fact]
    public void Restore_LoadsExistingTracksThroughDeckActions()
    {
        string track = System.IO.Path.GetTempFileName();
        try
        {
            var dispatcher = new FakeDispatcher();
            var store = new FakeDeckSessionStore(
                [new DeckSessionState(1, track, 128, 0.2)]);

            using var persistence = new DeckSessionPersistence(
                dispatcher, store, deckCount: 2, enableRetryTimer: false);

            // The constructor deliberately dispatches NOTHING: opening a track can take seconds on a
            // network share and this runs before the window exists. The load happens on the restore tick,
            // which the test drives directly.
            Assert.Empty(dispatcher.Dispatched);
            persistence.RetryPending();

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
    public void A_slow_restore_pass_is_not_re_entered_by_the_next_tick()
    {
        // Opening a track on a network share takes seconds, and the retry timer keeps ticking underneath.
        // A second pass must not walk the same still-pending entry and load the deck again — which is
        // exactly what happened in the field, six times per deck.
        string track = System.IO.Path.GetTempFileName();
        try
        {
            var dispatcher = new FakeDispatcher();
            var store = new FakeDeckSessionStore([new DeckSessionState(0, track, 128, 0.2)]);
            DeckSessionPersistence? persistence = null;
            int probes = 0;

            // Re-enter from inside the probe — the same entry is still pending there, because it is only
            // removed after the whole pass. That is precisely the window the timer used to fire into.
            persistence = new DeckSessionPersistence(
                dispatcher, store, deckCount: 2,
                fileExists: _ =>
                {
                    if (++probes == 1)
                        persistence!.RetryPending();
                    return true;
                },
                enableRetryTimer: false);

            using (persistence)
                persistence.RetryPending();

            Assert.Single(dispatcher.Dispatched.Where(a => a.Kind == PerformanceActionKind.DeckLoadTrack));
        }
        finally
        {
            File.Delete(track);
        }
    }

    [Fact]
    public void Constructor_TouchesNeitherTheEngineNorTheFilesystem()
    {
        // The whole point of the deferral: this type is built inside the composition root, before the
        // window exists. A reachability probe is as blocking as the load — File.Exists on a share that
        // has gone away waits for the SMB timeout — so neither may happen here.
        var dispatcher = new FakeDispatcher();
        var store = new FakeDeckSessionStore([new DeckSessionState(0, @"\\server\share\track.mp3", 128, 0.2)]);
        int probes = 0;

        using var persistence = new DeckSessionPersistence(
            dispatcher, store, deckCount: 2,
            fileExists: _ => { probes++; return true; },
            enableRetryTimer: false);

        Assert.Empty(dispatcher.Dispatched);
        Assert.Equal(0, probes);

        // ...and it all still happens, one tick later.
        persistence.RetryPending();

        Assert.Equal(1, probes);
        Assert.Contains(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckLoadTrack);
    }

    [Fact]
    public void Restore_ReAppliesAManuallySetDownbeat_WhenSaved()
    {
        string track = System.IO.Path.GetTempFileName();
        try
        {
            var dispatcher = new FakeDispatcher();
            var store = new FakeDeckSessionStore(
                [new DeckSessionState(1, track, 128, 0.2, DownbeatSeconds: 0.55)]);

            using var persistence = new DeckSessionPersistence(
                dispatcher, store, deckCount: 2, enableRetryTimer: false);
            persistence.RetryPending();

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
