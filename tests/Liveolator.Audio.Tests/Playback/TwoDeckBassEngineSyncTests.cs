using Liveolator.Audio.Playback;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// The professional Sync layer on the two-deck engine: explicit master/slave, and the continuous
/// phase-lock correction loop driven by <see cref="TwoDeckBassEngine.UpdateSync"/>. Exercised through
/// the fake BASS backend (no native audio), with positions set directly so the loop math is deterministic.
/// </summary>
public class TwoDeckBassEngineSyncTests
{
    private const double Bpm = 120.0;
    private const double BeatSeconds = 60.0 / Bpm;
    private const double Length = 100.0; // FakeBassMixerBackend default deck length

    private static TwoDeckBassEngine NewSyncedPair(
        out FakeBassMixerBackend backend, PhaseLockSettings? settings = null)
    {
        backend = new FakeBassMixerBackend();
        var mixer = new BassMixer(deckCount: TwoDeckBassEngine.Decks);
        var engine = new TwoDeckBassEngine(backend, mixer, phaseLock: settings);
        engine.Load(0, @"C:\master.wav"); // handle 100 — master
        engine.Load(1, @"C:\slave.wav");  // handle 101 — slave
        engine.SetDeckBaseBpm(0, Bpm);
        engine.SetDeckBaseBpm(1, Bpm);
        return engine;
    }

    // Place a deck's playhead at a given beat phase (beats past its first-beat anchor) via the backend.
    private static void SetBeatPhase(FakeBassMixerBackend backend, int handle, double beats)
        => backend.PositionFraction[handle] = beats * BeatSeconds / Length;

    [Fact]
    public void SyncOnce_BeatmatchesAndAlignsKick_WithoutEngagingContinuousLock()
    {
        using var engine = NewSyncedPair(out FakeBassMixerBackend backend);
        engine.SetDeckBaseBpm(0, 126.0);
        engine.SetDeckBaseBpm(1, 120.0);
        engine.SetDeckFirstBeat(0, 0.10);
        engine.SetDeckFirstBeat(1, 0.30);
        backend.PositionFraction[100] = 1.10 / Length;
        backend.PositionFraction[101] = 1.30 / Length;

        engine.SyncOnce(1);

        Assert.False(engine.IsSyncLocked(1));
        Assert.Null(engine.SyncMaster);
        Assert.Equal(126.0 / 120.0, backend.Rate[101], 6);
        var follower = new DeckPhase(backend.GetDeckPositionSeconds(101), 0.30, 126.0);
        var leader = new DeckPhase(backend.GetDeckPositionSeconds(100), 0.10, 126.0);
        Assert.Equal(0.0, PhaseAlignmentCalculator.BeatPhaseError(follower, leader), 6);
    }

    [Fact]
    public void EngagingSync_MakesTheOtherDeckTheMaster()
    {
        using var engine = NewSyncedPair(out _);

        engine.SetSyncLock(1, true);

        Assert.Equal(0, engine.SyncMaster);
        Assert.True(engine.IsSyncLocked(1));
        Assert.Equal(SyncLockState.Active, engine.SyncState(1));
    }

    [Fact]
    public void NoSecondDeck_HasNoMaster()
    {
        var backend = new FakeBassMixerBackend();
        using var engine = new TwoDeckBassEngine(backend, new BassMixer(deckCount: TwoDeckBassEngine.Decks));
        engine.Load(1, @"C:\only.wav");
        engine.SetDeckBaseBpm(1, Bpm);

        engine.SetSyncLock(1, true); // no valid master (slot 0 empty)

        Assert.Null(engine.SyncMaster);
    }

    [Fact]
    public void UpdateSync_WhenAligned_ReportsLocked_AndHoldsBeatmatchedRate()
    {
        using var engine = NewSyncedPair(out FakeBassMixerBackend backend);
        engine.SetSyncLock(1, true);
        // Both on the same beat phase => zero error.
        SetBeatPhase(backend, 100, 0.0);
        SetBeatPhase(backend, 101, 0.0);

        engine.UpdateSync(hostTimeTicks: 0);

        Assert.Equal(SyncLockState.Locked, engine.SyncState(1));
        Assert.Equal(1.0, backend.Rate[101], 9); // equal tempos, no correction
    }

    [Fact]
    public void UpdateSync_SlaveBehind_AppliesPositiveCorrectionWithinMax()
    {
        using var engine = NewSyncedPair(out FakeBassMixerBackend backend);
        engine.SetSyncLock(1, true);
        SetBeatPhase(backend, 100, 0.10); // master 0.1 beat ahead
        SetBeatPhase(backend, 101, 0.00); // slave behind

        engine.UpdateSync(0);

        Assert.Equal(SyncLockState.Active, engine.SyncState(1));
        Assert.Equal(1.0 + (0.10 * PhaseLockSettings.Default.Gain), backend.Rate[101], 9);
    }

    [Fact]
    public void UpdateSync_ClampsCorrectionToMax()
    {
        var hotGain = PhaseLockSettings.Default with { Gain = 1.0 };
        using var engine = NewSyncedPair(out FakeBassMixerBackend backend, hotGain);
        engine.SetSyncLock(1, true);
        SetBeatPhase(backend, 100, 0.10); // 0.1 * gain 1.0 = 0.1, clamped to MaxCorrection
        SetBeatPhase(backend, 101, 0.00);

        engine.UpdateSync(0);

        Assert.Equal(1.0 + PhaseLockSettings.Default.MaxCorrection, backend.Rate[101], 9);
    }

    [Fact]
    public void UpdateSync_LargeError_ReSnapsPlayhead_AndReportsDrifting()
    {
        using var engine = NewSyncedPair(out FakeBassMixerBackend backend);
        engine.SetSyncLock(1, true);
        SetBeatPhase(backend, 100, 0.35); // beyond the 0.25-beat re-snap threshold
        SetBeatPhase(backend, 101, 0.00);

        engine.UpdateSync(0);

        Assert.Equal(SyncLockState.Drifting, engine.SyncState(1));
        // The slave playhead is snapped forward 0.35 beat onto the master grid.
        Assert.Equal(0.35 * BeatSeconds / Length, backend.PositionFraction[101], 6);
    }

    [Fact]
    public void Release_ClearsMaster_RevertsRate_AndReportsOff()
    {
        using var engine = NewSyncedPair(out FakeBassMixerBackend backend);
        engine.SetPitch(1, 1.0, relative: false); // slave fader at +8%
        engine.SetSyncLock(1, true);

        engine.SetSyncLock(1, false);

        Assert.Null(engine.SyncMaster);
        Assert.Equal(SyncLockState.Off, engine.SyncState(1));
        Assert.Equal(1.08, backend.Rate[101], 6); // back to the manual pitch fader
    }

    [Fact]
    public void TryGetSyncMasterBeat_ReturnsMasterTempoAndContinuousBeat()
    {
        using var engine = NewSyncedPair(out FakeBassMixerBackend backend);
        engine.SetSyncLock(1, true);
        SetBeatPhase(backend, 100, 2.5); // master 2.5 beats past its anchor

        bool ok = engine.TryGetSyncMasterBeat(out double bpm, out double beat);

        Assert.True(ok);
        Assert.Equal(Bpm, bpm, precision: 6);
        Assert.Equal(2.5, beat, precision: 6);
    }

    [Fact]
    public void TryGetSyncMasterBeat_PitchedMaster_UsesBaseTempoForMediaPosition()
    {
        using var engine = NewSyncedPair(out FakeBassMixerBackend backend);
        engine.SetPitch(0, 1.0, relative: false); // master at +8%
        engine.SetSyncLock(1, true);
        SetBeatPhase(backend, 100, 2.5); // original-track position is still 2.5 beats

        bool ok = engine.TryGetSyncMasterBeat(out double bpm, out double beat);

        Assert.True(ok);
        Assert.Equal(Bpm * 1.08, bpm, precision: 6);
        Assert.Equal(2.5, beat, precision: 6);
    }

    [Fact]
    public void TryGetSyncMasterBeat_NoMaster_ReturnsFalse()
    {
        using var engine = NewSyncedPair(out _);
        // Sync not engaged => no master.
        Assert.False(engine.TryGetSyncMasterBeat(out _, out _));
    }
}
