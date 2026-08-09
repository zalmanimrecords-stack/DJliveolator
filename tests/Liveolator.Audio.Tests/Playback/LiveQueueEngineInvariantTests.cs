using System;
using System.Collections.Generic;
using System.Linq;
using Liveolator.Audio.Playback;
using Liveolator.Core.Beat;
using Liveolator.Core.Playlist;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// The live-queue invariant across the Core↔Audio seam: the REAL <see cref="LivePlaylist"/> (which owns
/// the rule) driving the REAL <see cref="PlaylistAudioPlayer"/> (which owns the advance trigger), over a
/// fake engine. <see cref="PlaylistAudioPlayerTests"/> covers the binding against a fake queue and
/// <c>LivePlaylistTests</c> covers the queue with no engine — neither proves the two assemblies agree,
/// which is the only thing standing between a queue edit and a silent floor.
/// </summary>
public sealed class LiveQueueEngineInvariantTests
{
    /// <summary>Fires the scheduled action immediately, so a quantized skip is testable without a clock.</summary>
    private sealed class ImmediateBeatScheduler : IBeatScheduler
    {
        public int ScheduleCount { get; private set; }

        public void Schedule(Quantize when, int everyN, Action onFire)
        {
            ScheduleCount++;
            onFire();
        }
    }

    private static LivePlaylist Queue(IBeatScheduler? scheduler = null)
        => new(scheduler ?? new ImmediateBeatScheduler(), NullLogger<LivePlaylist>.Instance);

    // Loads the queue and binds the player, returning both plus the engine with its bind-time calls
    // already cleared, so each test asserts only what its own action caused.
    private static (LivePlaylist Queue, FakeMultiDeckPlaybackEngine Engine, PlaylistAudioPlayer Player) Bound(
        int slot, params string[] tracks)
    {
        LivePlaylist queue = Queue();
        var engine = new FakeMultiDeckPlaybackEngine();
        queue.Load(tracks);
        var player = new PlaylistAudioPlayer(queue, engine, slot: slot);
        engine.Calls.Clear();
        return (queue, engine, player);
    }

    [Fact]
    public void DeckEnd_AdvancesTheQueueExactlyOnce()
    {
        (LivePlaylist queue, FakeMultiDeckPlaybackEngine engine, PlaylistAudioPlayer player) =
            Bound(0, "a.wav", "b.wav", "c.wav");
        using (player)
        {
            engine.RaiseDeckEnded(0);

            Assert.Equal("b.wav", queue.Now?.TrackPath);
            Assert.Equal("b.wav", engine.LoadedOn(0));
            // Exactly one load: a double advance would have skipped straight past b.wav to c.wav.
            Assert.Equal(1, engine.Calls.Count(call => call.StartsWith("Load(", StringComparison.Ordinal)));
            Assert.Equal(new[] { "c.wav" }, queue.Upcoming.Select(entry => entry.TrackPath));
        }
    }

    [Fact]
    public void DeckEnd_OnAnotherSlot_LeavesNowAndTheEngineAlone()
    {
        (LivePlaylist queue, FakeMultiDeckPlaybackEngine engine, PlaylistAudioPlayer player) =
            Bound(0, "a.wav", "b.wav");
        using (player)
        {
            engine.RaiseDeckEnded(1);

            Assert.Equal("a.wav", queue.Now?.TrackPath);
            Assert.Empty(engine.Calls);
        }
    }

    [Fact]
    public void EditingTheFuture_WhilePlaying_NeverTouchesTheDeck()
    {
        (LivePlaylist queue, FakeMultiDeckPlaybackEngine engine, PlaylistAudioPlayer player) =
            Bound(0, "a.wav", "b.wav", "c.wav");
        using (player)
        {
            queue.Append("d.wav");
            queue.InsertNext("e.wav");
            queue.Move(queue.Upcoming[^1].Id, 0);
            queue.RemoveFuture(queue.Upcoming[^1].Id);

            // Now is untouched and the deck was never reloaded — the whole point of Now/Next/Later.
            Assert.Equal("a.wav", queue.Now?.TrackPath);
            Assert.Empty(engine.Calls);
        }
    }

    [Fact]
    public void RemovingNow_IsRefused_AndTheDeckKeepsPlaying()
    {
        (LivePlaylist queue, FakeMultiDeckPlaybackEngine engine, PlaylistAudioPlayer player) =
            Bound(0, "a.wav", "b.wav");
        using (player)
        {
            queue.RemoveFuture(queue.Now!.Id);

            Assert.Equal("a.wav", queue.Now?.TrackPath);
            Assert.Empty(engine.Calls);
        }
    }

    [Fact]
    public void QuantizedSkip_AdvancesOnceThroughTheScheduler()
    {
        var scheduler = new ImmediateBeatScheduler();
        LivePlaylist queue = Queue(scheduler);
        var engine = new FakeMultiDeckPlaybackEngine();
        queue.Load(new[] { "a.wav", "b.wav", "c.wav" });
        using var player = new PlaylistAudioPlayer(queue, engine, slot: 0);
        engine.Calls.Clear();

        queue.SkipOn(Quantize.NextBar);

        Assert.Equal(1, scheduler.ScheduleCount);
        Assert.Equal("b.wav", queue.Now?.TrackPath);
        Assert.Equal(1, engine.Calls.Count(call => call.StartsWith("Load(", StringComparison.Ordinal)));
    }

    [Fact]
    public void RunningDry_StopsTheDeck_AndAFurtherEndLoadsNothing()
    {
        (LivePlaylist queue, FakeMultiDeckPlaybackEngine engine, PlaylistAudioPlayer player) = Bound(0, "a.wav");
        using (player)
        {
            engine.RaiseDeckEnded(0);

            Assert.Null(queue.Now);
            Assert.Contains("Stop(0)", engine.Calls);

            // An end-of-track on an already-empty queue must not resurrect a track or throw.
            engine.Calls.Clear();
            engine.RaiseDeckEnded(0);

            Assert.Null(queue.Now);
            Assert.DoesNotContain(engine.Calls, call => call.StartsWith("Load(", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AutoAdvanceOff_HoldsNowWhenTheDeckEnds()
    {
        (LivePlaylist queue, FakeMultiDeckPlaybackEngine engine, PlaylistAudioPlayer player) =
            Bound(0, "a.wav", "b.wav");
        using (player)
        {
            queue.SetAutoAdvance(false);
            engine.RaiseDeckEnded(0);

            Assert.Equal("a.wav", queue.Now?.TrackPath);
            Assert.Empty(engine.Calls);
        }
    }

    [Fact]
    public void Dispose_StopsTheBindingFollowingTheQueue()
    {
        (LivePlaylist queue, FakeMultiDeckPlaybackEngine engine, PlaylistAudioPlayer player) =
            Bound(0, "a.wav", "b.wav");
        player.Dispose();

        queue.SkipNow();
        engine.RaiseDeckEnded(0);

        Assert.Empty(engine.Calls);
    }

    [Fact]
    public void ARaceOfEndAndSkip_AdvancesOncePerEvent_NeverSkippingATrack()
    {
        (LivePlaylist queue, FakeMultiDeckPlaybackEngine engine, PlaylistAudioPlayer player) =
            Bound(0, "a.wav", "b.wav", "c.wav", "d.wav");
        using (player)
        {
            // The floor case: the track runs out at the same moment the performer hits skip.
            engine.RaiseDeckEnded(0);
            queue.SkipNow();

            Assert.Equal("c.wav", queue.Now?.TrackPath);
            Assert.Equal("c.wav", engine.LoadedOn(0));
            List<string> loads = engine.Calls
                .Where(call => call.StartsWith("Load(", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(new[] { "Load(0,b.wav)", "Load(0,c.wav)" }, loads);
        }
    }
}
