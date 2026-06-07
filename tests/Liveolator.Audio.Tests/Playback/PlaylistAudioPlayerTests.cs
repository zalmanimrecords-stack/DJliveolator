using System;
using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Audio;
using Liveolator.Core.Playlist;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// The NowChanged → load+play sequencing of the live-queue audio binding, exercised against a fake
/// engine (no native BASS). Covers the happy path, queue-exhausted stop, the tolerant degrade on a
/// bad track, the bind-time pickup of an existing Now, and unsubscribe on dispose.
/// </summary>
public sealed class PlaylistAudioPlayerTests
{
    private static QueueEntry Entry(string path) => new(path, Guid.NewGuid(), TrackState.Now);

    [Fact]
    public void NowChanged_LoadsAndPlaysOnBoundSlot()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        using var player = new PlaylistAudioPlayer(playlist, engine, slot: 1);

        playlist.RaiseNowChanged(Entry("a.wav"));

        Assert.Equal(new[] { "Load(1,a.wav)", "PlayPause(1)" }, engine.Calls);
        Assert.Equal("a.wav", engine.LoadedOn(1));
        Assert.True(engine.IsPlaying(1));
    }

    [Fact]
    public void NowChanged_DispatchesCataloguedBpmAndFirstBeatToTheDeck()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        var dispatcher = new PerformanceActionDispatcher(
            new IPerformanceActionHandler[] { new DeckActionHandler(engine) },
            NullLogger<PerformanceActionDispatcher>.Instance);
        using var player = new PlaylistAudioPlayer(
            playlist,
            dispatcher,
            engine,
            analysisResolver: path => path == "a.wav" ? new BpmResult(126.0, 0.9, 0.375) : null,
            slot: 1);

        playlist.RaiseNowChanged(Entry("a.wav"));

        Assert.Equal(126.0, engine.DeckBaseBpm(1), precision: 6);
        Assert.Equal(0.375, engine.DeckFirstBeat(1), precision: 6);
    }

    [Fact]
    public void NowChanged_WhenAutoPlayOff_LoadsButDoesNotPlay()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        using var player = new PlaylistAudioPlayer(playlist, engine, slot: 0, autoPlay: false);

        playlist.RaiseNowChanged(Entry("a.wav"));

        Assert.Equal(new[] { "Load(0,a.wav)" }, engine.Calls);
        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void NowChanged_ToNull_StopsTheDeck_WhenQueueExhausted()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        using var player = new PlaylistAudioPlayer(playlist, engine, slot: 0);

        playlist.RaiseNowChanged(Entry("a.wav"));
        engine.Calls.Clear();
        playlist.RaiseNowChanged(null);

        Assert.Equal(new[] { "Stop(0)" }, engine.Calls);
        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void NowChanged_AdvancesAcrossMultipleTracks()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        using var player = new PlaylistAudioPlayer(playlist, engine, slot: 0);

        playlist.RaiseNowChanged(Entry("a.wav"));
        playlist.RaiseNowChanged(Entry("b.wav"));

        Assert.Equal("b.wav", engine.LoadedOn(0));
        Assert.True(engine.IsPlaying(0));
    }

    [Fact]
    public void FailedLoad_IsSwallowed_AndTheQueueKeepsAdvancing()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine { ThrowOnLoadOf = "bad.wav" };
        using var player = new PlaylistAudioPlayer(playlist, engine, slot: 0);

        // A bad track must not throw out of the event handler...
        playlist.RaiseNowChanged(Entry("bad.wav"));
        // ...and the next track still plays.
        playlist.RaiseNowChanged(Entry("good.wav"));

        Assert.Equal("good.wav", engine.LoadedOn(0));
        Assert.True(engine.IsPlaying(0));
    }

    [Fact]
    public void Construction_PicksUpAnExistingNow()
    {
        var playlist = new FakeLivePlaylist { Now = Entry("already.wav") };
        var engine = new FakeMultiDeckPlaybackEngine();

        using var player = new PlaylistAudioPlayer(playlist, engine, slot: 0);

        Assert.Equal("already.wav", engine.LoadedOn(0));
        Assert.True(engine.IsPlaying(0));
    }

    [Fact]
    public void Dispose_UnsubscribesFromNowChanged()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        var player = new PlaylistAudioPlayer(playlist, engine, slot: 0);

        player.Dispose();
        playlist.RaiseNowChanged(Entry("a.wav"));

        Assert.Empty(engine.Calls);
    }

    [Fact]
    public void Construction_RejectsAnOutOfRangeSlot()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine(deckCount: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaylistAudioPlayer(playlist, engine, slot: 2));
    }

    // --- End-of-track auto-advance (A4) ---

    [Fact]
    public void DeckEnded_OnBoundSlot_NotifiesTheQueueToAdvance()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        using var player = new PlaylistAudioPlayer(playlist, engine, slot: 0);

        engine.RaiseDeckEnded(0);

        Assert.Equal(1, playlist.NotifyTrackEndedCount);
    }

    [Fact]
    public void DeckEnded_OnAnotherSlot_IsIgnored()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        using var player = new PlaylistAudioPlayer(playlist, engine, slot: 0);

        engine.RaiseDeckEnded(1); // a different deck ended — not this player's queue

        Assert.Equal(0, playlist.NotifyTrackEndedCount);
    }

    [Fact]
    public void Dispose_UnsubscribesFromDeckEnded()
    {
        var playlist = new FakeLivePlaylist();
        var engine = new FakeMultiDeckPlaybackEngine();
        var player = new PlaylistAudioPlayer(playlist, engine, slot: 0);

        player.Dispose();
        engine.RaiseDeckEnded(0);

        Assert.Equal(0, playlist.NotifyTrackEndedCount);
    }
}
