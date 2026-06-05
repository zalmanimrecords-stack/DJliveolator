using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Playlist;
using Liveolator.Core.Tests.Actions;
using Xunit;

namespace Liveolator.Core.Tests.Playlist;

public class PlaylistActionHandlerTests
{
    private readonly ImmediateBeatScheduler _scheduler = new();
    private readonly LivePlaylist _playlist;
    private readonly PlaylistActionHandler _handler;

    public PlaylistActionHandlerTests()
    {
        _playlist = new LivePlaylist(_scheduler, new CapturingLogger<LivePlaylist>());
        _handler = new PlaylistActionHandler(_playlist, new CapturingLogger<PlaylistActionHandler>());
    }

    private void Handle(PerformanceActionKind kind, string? argument = null, int slot = 0)
        => _handler.Handle(new PerformanceAction(kind, Argument: argument, Slot: slot));

    [Fact]
    public void HandledKinds_CoverThePlaylistActions()
    {
        Assert.Equal(4, _handler.HandledKinds.Count);
        Assert.Contains(PerformanceActionKind.PlaylistSkipOnNextBar, _handler.HandledKinds);
    }

    [Fact]
    public void InsertTrackNext_InsertsArgumentPathAsNext()
    {
        _playlist.Load(new[] { "a.mp3" });

        Handle(PerformanceActionKind.PlaylistInsertTrackNext, argument: "x.mp3");

        Assert.Equal("x.mp3", _playlist.Upcoming[0].TrackPath);
    }

    [Fact]
    public void InsertTrackNext_WithoutArgument_IsIgnored()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });

        Handle(PerformanceActionKind.PlaylistInsertTrackNext, argument: null);

        Assert.Equal("b.mp3", _playlist.Upcoming[0].TrackPath); // unchanged
    }

    [Fact]
    public void MoveTrack_ReordersByIdAndSlot()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        Guid lastId = _playlist.Upcoming[^1].Id; // c

        Handle(PerformanceActionKind.PlaylistMoveTrack, argument: lastId.ToString(), slot: 0);

        Assert.Equal("c.mp3", _playlist.Upcoming[0].TrackPath);
    }

    [Fact]
    public void MoveTrack_InvalidId_IsIgnored()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });

        var exception = Record.Exception(() => Handle(PerformanceActionKind.PlaylistMoveTrack, argument: "not-a-guid"));

        Assert.Null(exception);
        Assert.Equal("b.mp3", _playlist.Upcoming[0].TrackPath);
    }

    [Fact]
    public void RemoveFutureTrack_RemovesById()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        Guid bId = _playlist.Upcoming[0].Id;

        Handle(PerformanceActionKind.PlaylistRemoveFutureTrack, argument: bId.ToString());

        Assert.Single(_playlist.Upcoming);
        Assert.Equal("c.mp3", _playlist.Upcoming[0].TrackPath);
    }

    [Fact]
    public void SkipOnNextBar_DefersThroughTheScheduler()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });

        Handle(PerformanceActionKind.PlaylistSkipOnNextBar);

        Assert.Equal(Quantize.NextBar, _scheduler.LastWhen);
        Assert.Equal("b.mp3", _playlist.Now!.TrackPath); // immediate fake scheduler advanced it
    }

    [Fact]
    public void EndToEnd_DispatcherRoutesSkipToThePlaylist()
    {
        _playlist.Load(new[] { "a.mp3", "b.mp3" });
        using var dispatcher = new PerformanceActionDispatcher(
            new[] { _handler }, new CapturingLogger<PerformanceActionDispatcher>());

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.PlaylistSkipOnNextBar));

        Assert.Equal("b.mp3", _playlist.Now!.TrackPath);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new PlaylistActionHandler(null!, new CapturingLogger<PlaylistActionHandler>()));
        Assert.Throws<ArgumentNullException>(() => new PlaylistActionHandler(_playlist, null!));
    }
}
