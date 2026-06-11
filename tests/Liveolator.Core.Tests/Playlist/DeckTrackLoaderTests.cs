using Liveolator.Core.Actions;
using Liveolator.Core.Playlist;
using Xunit;

namespace Liveolator.Core.Tests.Playlist;

public sealed class DeckTrackLoaderTests
{
    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        private readonly HashSet<int> _playingSlots = new();
        public List<PerformanceAction> Dispatched { get; } = new();
        public void SetPlaying(int slot) => _playingSlots.Add(slot);
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => kind == PerformanceActionKind.DeckPlayPause
                ? new ActionFeedbackState(IsActive: _playingSlots.Contains(slot), IsAvailable: true, Value: 0)
                : ActionFeedbackState.Unavailable;
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged { add { } remove { } }
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
    }

    [Fact]
    public void Load_OnAnIdleDeck_DispatchesLoadAndDownbeatAnchor()
    {
        var dispatcher = new RecordingDispatcher();
        var loader = new DeckTrackLoader(dispatcher, _ => true);

        DeckLoadResult result = loader.Load(1, "/m/a.wav", bpm: 126.0, firstBeatSeconds: 0.5);

        Assert.Equal(DeckLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(2, dispatcher.Dispatched.Count);
        PerformanceAction load = dispatcher.Dispatched[0];
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, load.Kind);
        Assert.Equal(1, load.Slot);
        Assert.Equal("/m/a.wav", load.Argument);
        Assert.Equal(126.0, load.Value, precision: 6);
        PerformanceAction anchor = dispatcher.Dispatched[1];
        Assert.Equal(PerformanceActionKind.DeckSetFirstBeat, anchor.Kind);
        Assert.Equal(1, anchor.Slot);
        Assert.Equal(0.5, anchor.Value, precision: 6);
    }

    [Fact]
    public void Load_OnAPlayingDeck_AppendsToThatDecksQueueInstead()
    {
        var dispatcher = new RecordingDispatcher();
        dispatcher.SetPlaying(1);
        var loader = new DeckTrackLoader(dispatcher, _ => true);

        DeckLoadResult result = loader.Load(1, "/m/a.wav", bpm: 126.0);

        Assert.Equal(DeckLoadOutcome.Queued, result.Outcome);
        PerformanceAction append = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.PlaylistAppendTrack, append.Kind);
        Assert.Equal(1, append.Slot);
        Assert.Equal("/m/a.wav", append.Argument);
        Assert.Contains("queue", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_WhileTheOtherDeckPlays_StillLoadsDirectly()
    {
        var dispatcher = new RecordingDispatcher();
        dispatcher.SetPlaying(0); // deck A plays; loading onto deck B must not queue
        var loader = new DeckTrackLoader(dispatcher, _ => true);

        DeckLoadResult result = loader.Load(1, "/m/a.wav", bpm: 0);

        Assert.Equal(DeckLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, dispatcher.Dispatched[0].Kind);
    }

    [Fact]
    public void Load_WhenTheFileIsUnreachable_DispatchesNothingAndSaysWhy()
    {
        var dispatcher = new RecordingDispatcher();
        var loader = new DeckTrackLoader(dispatcher, _ => false);

        DeckLoadResult result = loader.Load(0, @"S:\offline\track.mp3", bpm: 140.0);

        Assert.Equal(DeckLoadOutcome.FileMissing, result.Outcome);
        Assert.Empty(dispatcher.Dispatched);
        Assert.Contains("missing", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"S:\offline\track.mp3", result.Message);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new DeckTrackLoader(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => new DeckTrackLoader(new RecordingDispatcher(), null!));
    }
}
