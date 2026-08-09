using Liveolator.Core.Playlist;

namespace Liveolator.Core.Tests.Playlist;

public sealed class PlayedHistoryTrackerTests
{
    [Fact]
    public void StartsEmptyAtTheCurrentQueuePosition()
    {
        QueueEntry now = Entry("a", TrackState.Now);
        var tracker = new PlayedHistoryTracker(now, [Entry("b", TrackState.Next)]);

        Assert.Empty(tracker.History);
    }

    [Fact]
    public void SequentialAdvancesRecordPlayedEntriesMostRecentFirst()
    {
        QueueEntry a = Entry("a", TrackState.Now);
        QueueEntry b = Entry("b", TrackState.Next);
        QueueEntry c = Entry("c", TrackState.Later);
        var tracker = new PlayedHistoryTracker(a, [b, c]);

        Assert.True(tracker.Observe(b with { State = TrackState.Now }, [c with { State = TrackState.Next }]));
        Assert.True(tracker.Observe(c with { State = TrackState.Now }, []));

        Assert.Equal(new[] { "b", "a" }, tracker.History.Select(entry => entry.TrackPath));
        Assert.All(tracker.History, entry => Assert.Equal(TrackState.Played, entry.State));
    }

    [Fact]
    public void ReloadResetsExistingHistory()
    {
        QueueEntry a = Entry("a", TrackState.Now);
        QueueEntry b = Entry("b", TrackState.Next);
        var tracker = new PlayedHistoryTracker(a, [b]);
        tracker.Observe(b with { State = TrackState.Now }, []);

        bool changed = tracker.Observe(Entry("x", TrackState.Now), [Entry("y", TrackState.Next)]);

        Assert.True(changed);
        Assert.Empty(tracker.History);
    }

    [Fact]
    public void ReloadWithoutHistoryDoesNotReportAVisibleChange()
    {
        var tracker = new PlayedHistoryTracker(Entry("a", TrackState.Now), []);

        bool changed = tracker.Observe(Entry("x", TrackState.Now), []);

        Assert.False(changed);
        Assert.Empty(tracker.History);
    }

    private static QueueEntry Entry(string path, TrackState state)
        => new(path, Guid.NewGuid(), state);
}
