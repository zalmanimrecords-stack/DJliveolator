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
