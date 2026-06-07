using Liveolator.Core.Audio.Sync;
using Xunit;

namespace Liveolator.Core.Tests.Audio.Sync;

/// <summary>
/// The continuous phase-lock control law: the proportional micro-correction that keeps a synced deck
/// beat-locked over time. Pure math, so every case is asserted deterministically against a known tempo.
/// </summary>
public class PhaseLockControllerTests
{
    private static readonly PhaseLockSettings Settings = PhaseLockSettings.Default;

    // 120 BPM => 0.5 s per beat. Both decks share tempo and anchor unless a test says otherwise, so the
    // beatmatched base rate is 1.0 and any rate change is purely the phase correction.
    private const double Bpm = 120.0;
    private const double BeatSeconds = 60.0 / Bpm;

    private static DeckPhase At(double positionSeconds) => new(positionSeconds, FirstBeatSeconds: 0.0, Bpm);

    [Fact]
    public void WithinLockTolerance_HoldsBeatmatchedRate_AndReportsLocked()
    {
        // 0.01 beat error is inside the 0.02-beat lock zone: no correction, rate stays exactly base.
        DeckPhase master = At(0.01 * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(slave, master, beatmatchedRate: 1.0, Settings);

        Assert.Equal(SyncLockState.Locked, result.State);
        Assert.Equal(1.0, result.EffectiveRate, precision: 9);
        Assert.False(result.RequiresReSnap);
    }

    [Fact]
    public void SlaveBehindMaster_SpeedsUp()
    {
        // Master 0.1 beat ahead of the slave => positive error => slave must run faster than base.
        DeckPhase master = At(0.1 * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(slave, master, beatmatchedRate: 1.0, Settings);

        Assert.Equal(SyncLockState.Active, result.State);
        Assert.True(result.EffectiveRate > 1.0);
        Assert.Equal(1.0 + (0.1 * Settings.Gain), result.EffectiveRate, precision: 9);
        Assert.False(result.RequiresReSnap);
    }

    [Fact]
    public void SlaveAheadOfMaster_SlowsDown()
    {
        // Slave 0.1 beat ahead => negative error => slave must run slower than base.
        DeckPhase master = At(0.0);
        DeckPhase slave = At(0.1 * BeatSeconds);

        PhaseLockCorrection result = PhaseLockController.Correct(slave, master, beatmatchedRate: 1.0, Settings);

        Assert.Equal(SyncLockState.Active, result.State);
        Assert.True(result.EffectiveRate < 1.0);
        Assert.Equal(1.0 - (0.1 * Settings.Gain), result.EffectiveRate, precision: 9);
    }

    [Fact]
    public void CorrectionIsClampedToMaxCorrection()
    {
        // A 0.2-beat error with gain 0.01 would ask for 0.002 — well under the 0.03 ceiling — so to test
        // the clamp we use a large gain. error 0.2 * gain 1.0 = 0.2, clamped to +MaxCorrection.
        var hotGain = Settings with { Gain = 1.0 };
        DeckPhase master = At(0.2 * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(slave, master, beatmatchedRate: 1.0, hotGain);

        Assert.Equal(1.0 + Settings.MaxCorrection, result.EffectiveRate, precision: 9);
        Assert.Equal(SyncLockState.Active, result.State); // 0.2 < 0.25 re-snap threshold
    }

    [Fact]
    public void BeyondReSnapThreshold_RequestsBeatSnap_AndReportsDrifting()
    {
        // 0.35-beat error exceeds the 0.25-beat re-snap threshold: too far to ride back on pitch, so a
        // one-shot beat-snap seek is requested. The wrapped error is positive (master ahead), so the
        // shortest snap is forward.
        DeckPhase master = At(0.35 * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(slave, master, beatmatchedRate: 1.0, Settings);

        Assert.Equal(SyncLockState.Drifting, result.State);
        Assert.True(result.RequiresReSnap);
        Assert.Equal(0.35 * BeatSeconds, result.ReSnapSeconds, precision: 6);
        // The micro-correction still rides this tick so there is no audible gap before the seek lands.
        Assert.True(result.EffectiveRate > 1.0);
    }

    [Fact]
    public void ReportsSignedErrorInBeats()
    {
        DeckPhase master = At(0.1 * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(slave, master, beatmatchedRate: 1.0, Settings);

        Assert.Equal(0.1, result.ErrorBeats, precision: 6);
    }

    [Fact]
    public void AppliesCorrectionRelativeToBeatmatchedRate_NotOne()
    {
        // A follower at a different base tempo runs at a beatmatched rate != 1.0; the correction is added
        // on top of that rate, never replacing it.
        DeckPhase master = At(0.1 * BeatSeconds);
        DeckPhase slave = At(0.0);
        const double beatmatched = 0.94;

        PhaseLockCorrection result = PhaseLockController.Correct(slave, master, beatmatched, Settings);

        Assert.Equal(beatmatched + (0.1 * Settings.Gain), result.EffectiveRate, precision: 9);
    }
}
