using System;
using Liveolator.Audio.Playback;
using Liveolator.Core.Playlist;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// The preload trigger: on each NowChanged the next upcoming track is warmed, a live reorder
/// supersedes the in-flight preload, an empty future clears it, and a failing preload degrades
/// without throwing. Exercised against a fake preloader (no native audio).
/// </summary>
public sealed class NextTrackPreloaderTests
{
    private static QueueEntry Upcoming(string path) => new(path, Guid.NewGuid(), TrackState.Next);

    [Fact]
    public void NowChanged_PreloadsTheNextUpcomingTrack()
    {
        var playlist = new FakeLivePlaylist { Upcoming = new[] { Upcoming("next.wav"), Upcoming("later.wav") } };
        var preloader = new FakeDeckPreloader();
        using var sut = new NextTrackPreloader(playlist, preloader);
        preloader.Requests.Clear(); // ignore the bind-time warm

        playlist.RaiseNowChanged(new QueueEntry("now.wav", Guid.NewGuid(), TrackState.Now));

        Assert.Equal(new[] { "next.wav" }, preloader.Requests);
    }

    [Fact]
    public void Construction_WarmsTheCurrentNextTrack()
    {
        var playlist = new FakeLivePlaylist { Upcoming = new[] { Upcoming("next.wav") } };
        var preloader = new FakeDeckPreloader();

        using var sut = new NextTrackPreloader(playlist, preloader);

        Assert.Equal(new[] { "next.wav" }, preloader.Requests);
    }

    [Fact]
    public void EmptyFuture_ClearsThePreload()
    {
        var playlist = new FakeLivePlaylist { Upcoming = Array.Empty<QueueEntry>() };
        var preloader = new FakeDeckPreloader();
        using var sut = new NextTrackPreloader(playlist, preloader);
        preloader.Requests.Clear();

        playlist.RaiseNowChanged(null);

        Assert.Equal(new string?[] { null }, preloader.Requests);
    }

    [Fact]
    public void LiveReorder_SupersedesTheInFlightPreload()
    {
        var playlist = new FakeLivePlaylist { Upcoming = new[] { Upcoming("first.wav") } };
        var preloader = new FakeDeckPreloader();
        using var sut = new NextTrackPreloader(playlist, preloader);
        preloader.Requests.Clear();

        // The user reorders the future so a different track is now next, then the queue notifies.
        playlist.Upcoming = new[] { Upcoming("reordered.wav") };
        playlist.RaiseNowChanged(playlist.Now);

        Assert.Equal(new[] { "reordered.wav" }, preloader.Requests);
    }

    [Fact]
    public void FailingPreload_IsSwallowed()
    {
        var playlist = new FakeLivePlaylist { Upcoming = new[] { Upcoming("boom.wav") } };
        var preloader = new FakeDeckPreloader { ThrowOnPreloadOf = "boom.wav" };

        // Must not throw out of construction or the event handler.
        using var sut = new NextTrackPreloader(playlist, preloader);
        playlist.RaiseNowChanged(null);
    }

    [Fact]
    public void Dispose_UnsubscribesFromNowChanged()
    {
        var playlist = new FakeLivePlaylist { Upcoming = new[] { Upcoming("next.wav") } };
        var preloader = new FakeDeckPreloader();
        var sut = new NextTrackPreloader(playlist, preloader);
        preloader.Requests.Clear();

        sut.Dispose();
        playlist.RaiseNowChanged(null);

        Assert.Empty(preloader.Requests);
    }
}
