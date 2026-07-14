using Liveolator.Core.Actions;
using Liveolator.Core.Playlist;
using Xunit;

namespace Liveolator.Core.Tests.Playlist;

public sealed class DeckTrackLoaderTests
{
    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        private readonly HashSet<int> _playingSlots = new();
        private readonly HashSet<int> _loadFailSlots = new();
        public List<PerformanceAction> Dispatched { get; } = new();
        public void SetPlaying(int slot) => _playingSlots.Add(slot);
        // Simulate the engine failing to open the file on a slot: the DeckActionHandler raises the
        // DeckLoadTrack feedback as unavailable in that case (the real signal the loader now checks).
        public void FailLoadOnSlot(int slot) => _loadFailSlots.Add(slot);
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
        {
            if (kind == PerformanceActionKind.DeckPlayPause)
                return new ActionFeedbackState(IsActive: _playingSlots.Contains(slot), IsAvailable: true, Value: 0);
            if (kind == PerformanceActionKind.DeckLoadTrack)
            {
                // Mirror the handler: once a DeckLoadTrack was dispatched for this slot the load feedback is
                // available, unless the slot was set to fail (engine could not open the file).
                bool dispatched = Dispatched.Exists(
                    a => a.Kind == PerformanceActionKind.DeckLoadTrack && a.Slot == slot);
                bool available = dispatched && !_loadFailSlots.Contains(slot);
                return new ActionFeedbackState(IsActive: available, IsAvailable: available, Value: 0);
            }
            return ActionFeedbackState.Unavailable;
        }
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
    public void Load_WithReplacePlaying_LoadsOverAPlayingDeck_InsteadOfQueueing()
    {
        // An audition (the library "Play"): the deck is already playing, but replacePlaying replaces it
        // rather than queueing behind it — the fix for a second Play being ignored.
        var dispatcher = new RecordingDispatcher();
        dispatcher.SetPlaying(0);
        var loader = new DeckTrackLoader(dispatcher, _ => true);

        DeckLoadResult result = loader.Load(0, "/m/b.wav", bpm: 120.0, replacePlaying: true);

        Assert.Equal(DeckLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, dispatcher.Dispatched[0].Kind);
        Assert.DoesNotContain(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.PlaylistAppendTrack);
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
    public void Load_WhenThePresentFileFailsToOpen_ReportsLoadFailed_AndSkipsTheDownbeatAnchor()
    {
        // The file passes the reachability probe, but the engine can't open it (corrupt/unsupported, or a
        // missing native effects library). The handler reports the load as unavailable; the loader must
        // surface that instead of a success message that contradicts the deck's "couldn't load" state.
        var dispatcher = new RecordingDispatcher();
        dispatcher.FailLoadOnSlot(0);
        var loader = new DeckTrackLoader(dispatcher, _ => true);

        DeckLoadResult result = loader.Load(0, @"C:\music\corrupt.flac", bpm: 128.0, firstBeatSeconds: 0.5);

        Assert.Equal(DeckLoadOutcome.LoadFailed, result.Outcome);
        // The load was attempted, but the downbeat anchor is NOT dispatched for a deck that never loaded.
        PerformanceAction load = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, load.Kind);
        Assert.DoesNotContain(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetFirstBeat);
        Assert.Contains("Couldn't load", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\music\corrupt.flac", result.Message);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new DeckTrackLoader(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => new DeckTrackLoader(new RecordingDispatcher(), null!));
    }
}
