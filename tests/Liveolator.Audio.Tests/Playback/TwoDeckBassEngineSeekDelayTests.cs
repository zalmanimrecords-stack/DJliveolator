using Liveolator.Audio.Playback;
using Liveolator.Core.Audio.Sync;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// A sync snap seeks the follower while the leader plays, so the mixer buffer is not flushed (that would skip
/// the leader) and the new position is heard only once the audio already buffered has played out — while the
/// leader keeps moving. Each snap must land where the leader's grid WILL be: ahead by that delay times the
/// follower's rate. Differential: the same scenario run with and without buffered audio.
/// </summary>
public class TwoDeckBassEngineSeekDelayTests
{
    private const double Length = 100.0; // FakeBassMixerBackend default deck length
    private const double LeaderBpm = 126.0;
    private const double FollowerBpm = 120.0;
    private const double SyncedRate = LeaderBpm / FollowerBpm;
    private const double Buffered = 0.035;
    private const double FollowerStart = 20.13; // 0.26 of a follower beat past its grid
    private const int LeaderHandle = 100;
    private const int FollowerHandle = 101;

    [Fact]
    public void EngageSnap_WhileTheLeaderPlays_LandsAheadByTheBufferedAudio_TimesTheFollowerRate()
    {
        Assert.Equal(Buffered * SyncedRate, EngageWhilePlaying(Buffered) - EngageWhilePlaying(0.0), precision: 9);
    }

    [Fact]
    public void ArmedStartBarSnap_LandsAheadByTheBufferedAudio_TimesTheFollowerRate()
    {
        Assert.Equal(Buffered * SyncedRate, ArmedStart(Buffered) - ArmedStart(0.0), precision: 9);
    }

    [Fact]
    public void ReSnap_WhileTheLeaderPlays_LandsAheadByTheBufferedAudio_TimesTheFollowerRate()
    {
        Assert.Equal(Buffered * SyncedRate, ReSnap(Buffered) - ReSnap(0.0), precision: 9);
    }

    [Fact]
    public void Snap_WithNoOtherDeckPlaying_IsNotCompensated()
    {
        // No other deck playing: the seek flushes the buffer, so it is heard at once — and the leader is still.
        double uncompensated = QuantizeAgainstAPausedLeader(0.0);

        Assert.NotEqual(FollowerStart, uncompensated, precision: 6); // it did snap
        Assert.Equal(uncompensated, QuantizeAgainstAPausedLeader(Buffered), precision: 9);
    }

    // Leader playing at 126 BPM, follower at 120 BPM (Sync runs it at 1.05x), both grids vouched for.
    private static TwoDeckBassEngine NewPair(double bufferedSeconds, out FakeBassMixerBackend backend)
    {
        backend = new FakeBassMixerBackend { BufferedSeconds = bufferedSeconds };
        var engine = new TwoDeckBassEngine(backend, new BassMixer(deckCount: TwoDeckBassEngine.Decks));
        engine.Load(0, @"C:\leader.wav");
        engine.Load(1, @"C:\follower.wav");
        engine.SetDeckBaseBpm(0, LeaderBpm);
        engine.SetDeckBaseBpm(1, FollowerBpm);
        engine.SetDeckPhaseSyncReady(0, true);
        engine.SetDeckPhaseSyncReady(1, true);
        backend.PositionFraction[LeaderHandle] = 10.0 / Length;
        backend.PositionFraction[FollowerHandle] = FollowerStart / Length;
        engine.PlayPause(0);
        return engine;
    }

    private static double EngageWhilePlaying(double bufferedSeconds)
    {
        using TwoDeckBassEngine engine = NewPair(bufferedSeconds, out FakeBassMixerBackend backend);
        engine.PlayPause(1);
        engine.SetSyncLock(1, true);
        return backend.GetDeckPositionSeconds(FollowerHandle);
    }

    private static double ArmedStart(double bufferedSeconds)
    {
        using TwoDeckBassEngine engine = NewPair(bufferedSeconds, out FakeBassMixerBackend backend);
        // Both bar anchors known and the follower paused, so the engage and the armed start bar-snap.
        engine.SetDeckDownbeat(0, 0.20);
        engine.SetDeckDownbeat(1, 0.30);
        engine.SetSyncLock(1, true);
        engine.PlayPause(1);
        return backend.GetDeckPositionSeconds(FollowerHandle);
    }

    private static double ReSnap(double bufferedSeconds)
    {
        using TwoDeckBassEngine engine = NewPair(bufferedSeconds, out FakeBassMixerBackend backend);
        engine.PlayPause(1);
        engine.SetSyncLock(1, true);
        backend.PositionFraction[FollowerHandle] = 19.825 / Length; // 0.35 beat behind: past the re-snap threshold

        engine.UpdateSync(hostTimeTicks: 0);

        Assert.Equal(SyncLockState.Drifting, engine.SyncState(1));
        return backend.GetDeckPositionSeconds(FollowerHandle);
    }

    private static double QuantizeAgainstAPausedLeader(double bufferedSeconds)
    {
        using TwoDeckBassEngine engine = NewPair(bufferedSeconds, out FakeBassMixerBackend backend);
        engine.PlayPause(0);
        engine.PlayPause(1);
        engine.SetQuantize(1, true);
        return backend.GetDeckPositionSeconds(FollowerHandle);
    }
}
