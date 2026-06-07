using Liveolator.Core.Beat;
using Liveolator.Core.Playlist;
using Liveolator.Core.Tests.Actions;
using Xunit;

namespace Liveolator.Core.Tests.Playlist;

public class LivePlaylistTests
{
    private readonly ImmediateBeatScheduler _scheduler = new();
    private readonly LivePlaylist _playlist;
    private int _nowChangedCount;
    private int _changedCount;

    public LivePlaylistTests()
    {
        _playlist = new LivePlaylist(_scheduler, new CapturingLogger<LivePlaylist>());
        _playlist.NowChanged += (_, _) => _nowChangedCount++;
        _playlist.Changed += (_, _) => _changedCount++;
    }

    [Fact]
    public void Load_PutsFirstInNow_RestUpcomingWithStates()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });

        Assert.Equal("a.mp3", _playlist.Now!.TrackPath);
        Assert.Equal(TrackState.Next, _playlist.Upcoming[0].State);
        Assert.Equal("b.mp3", _playlist.Upcoming[0].TrackPath);
        Assert.Equal(TrackState.Later, _playlist.Upcoming[1].State);
    }

    [Fact]
    public void InsertNext_BecomesNext_WithoutDisturbingNow()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });
        int afterLoad = _nowChangedCount;

        _playlist.InsertNext("x.mp3");

        Assert.Equal("a.mp3", _playlist.Now!.TrackPath);     // Now untouched
        Assert.Equal("x.mp3", _playlist.Upcoming[0].TrackPath); // inserted as Next
        Assert.Equal(afterLoad, _nowChangedCount);            // editing the future raises nothing
    }

    [Fact]
    public void Append_AddsToEndOfLater()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });
        _playlist.Append("z.mp3");

        Assert.Equal("z.mp3", _playlist.Upcoming[^1].TrackPath);
    }

    [Fact]
    public void Move_ReordersUpcoming()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        Guid lastId = _playlist.Upcoming[^1].Id; // c

        _playlist.Move(lastId, 0);

        Assert.Equal("c.mp3", _playlist.Upcoming[0].TrackPath);
    }

    [Fact]
    public void Move_StaleId_IsIgnored()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });

        var exception = Record.Exception(() => _playlist.Move(Guid.NewGuid(), 0));

        Assert.Null(exception);
        Assert.Equal("b.mp3", _playlist.Upcoming[0].TrackPath); // unchanged
    }

    [Fact]
    public void RemoveFuture_RemovesUpcomingEntry()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        Guid bId = _playlist.Upcoming[0].Id;

        _playlist.RemoveFuture(bId);

        Assert.Single(_playlist.Upcoming);
        Assert.Equal("c.mp3", _playlist.Upcoming[0].TrackPath);
    }

    [Fact]
    public void RemoveFuture_CannotRemoveNow()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });
        Guid nowId = _playlist.Now!.Id;

        _playlist.RemoveFuture(nowId);

        Assert.Equal("a.mp3", _playlist.Now!.TrackPath); // still playing
    }

    [Fact]
    public void SkipNow_AdvancesToNext_AndRaisesNowChanged()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });
        int afterLoad = _nowChangedCount;

        _playlist.SkipNow();

        Assert.Equal("b.mp3", _playlist.Now!.TrackPath);
        Assert.Empty(_playlist.Upcoming);
        Assert.Equal(afterLoad + 1, _nowChangedCount);
    }

    [Fact]
    public void SkipNow_AtEndOfQueue_LeavesNowNull()
    {
        _playlist.Load(new[] { "a.mp3" });

        _playlist.SkipNow();

        Assert.Null(_playlist.Now);
    }

    [Fact]
    public void SkipOn_SchedulesThroughTheBeatScheduler_AndAdvances()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });

        _playlist.SkipOn(Quantize.NextBar, everyN: 2);

        Assert.Equal(Quantize.NextBar, _scheduler.LastWhen);
        Assert.Equal(2, _scheduler.LastEveryN);
        Assert.Equal("b.mp3", _playlist.Now!.TrackPath); // immediate fake scheduler advanced it
    }

    [Fact]
    public void NotifyTrackEnded_Advances_WhenAutoAdvanceOn()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });

        _playlist.NotifyTrackEnded();

        Assert.Equal("b.mp3", _playlist.Now!.TrackPath);
    }

    [Fact]
    public void NotifyTrackEnded_IsNoOp_WhenAutoAdvanceOff()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });
        _playlist.SetAutoAdvance(false);

        _playlist.NotifyTrackEnded();

        Assert.Equal("a.mp3", _playlist.Now!.TrackPath); // stayed put
    }

    [Fact]
    public void Changed_FiresOnEveryMutationThatAltersTheSet()
    {
        // Each editing operation that changes Now or the upcoming order/contents must signal a save.
        _playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        Assert.Equal(1, _changedCount); // load

        _playlist.Append("d.mp3");
        Assert.Equal(2, _changedCount); // append

        _playlist.InsertNext("x.mp3");
        Assert.Equal(3, _changedCount); // insert

        Guid lastId = _playlist.Upcoming[^1].Id;
        _playlist.Move(lastId, 0);
        Assert.Equal(4, _changedCount); // move

        _playlist.RemoveFuture(lastId);
        Assert.Equal(5, _changedCount); // remove

        _playlist.SkipNow();
        Assert.Equal(6, _changedCount); // advance
    }

    [Fact]
    public void Changed_DoesNotFire_OnNoOpEdits()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });
        int afterLoad = _changedCount;

        _playlist.Move(Guid.NewGuid(), 0);        // stale id
        _playlist.RemoveFuture(Guid.NewGuid());   // stale id
        _playlist.RemoveFuture(_playlist.Now!.Id); // Now is protected

        Assert.Equal(afterLoad, _changedCount);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new LivePlaylist(null!, new CapturingLogger<LivePlaylist>()));
        Assert.Throws<ArgumentNullException>(() => new LivePlaylist(_scheduler, null!));
    }
}
