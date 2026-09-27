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

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void InsideLockZone_ReportsLocked_ButStillCorrectsTowardZero(double sign)
    {
        // The lock zone only names the state. A residual inside it is still pulled in, so a locked pair
        // settles on zero error instead of resting anywhere inside the zone.
        double errorBeats = sign * Settings.LockToleranceBeats / 2.0;
        DeckPhase master = At(errorBeats * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(slave, master, beatmatchedRate: 1.0, Settings);

        Assert.Equal(SyncLockState.Locked, result.State);
        Assert.Equal(1.0 + (errorBeats * Settings.Gain), result.EffectiveRate, precision: 9);
        Assert.Equal(Math.Sign(errorBeats), Math.Sign(result.EffectiveRate - 1.0));
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
        // Below the re-snap threshold the default gain never reaches the 0.03 ceiling, so to test the
        // clamp we use a large gain. error 0.2 * gain 1.0 = 0.2, clamped to +MaxCorrection.
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
    public void JustOutsideEnterZone_WhenAlreadyLocked_HoldsTheLockedLabel_AndStillCorrects()
    {
        // Error sits between the tight enter tolerance and the wider exit tolerance: a deck that is ALREADY
        // Locked keeps the label (hysteresis), so a deck resting on the boundary does not flicker the badge.
        // The rate follows the error either way — the correction is continuous, so there is no step to chatter.
        double errorBeats = (Settings.LockToleranceBeats + Settings.ExitLockToleranceBeats) / 2.0;
        DeckPhase master = At(errorBeats * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(
            slave, master, beatmatchedRate: 1.0, Settings, previousState: SyncLockState.Locked);

        Assert.Equal(SyncLockState.Locked, result.State);
        Assert.Equal(1.0 + (errorBeats * Settings.Gain), result.EffectiveRate, precision: 9);
    }

    [Fact]
    public void JustOutsideEnterZone_WhenActive_StaysActive_AndCorrects()
    {
        // Same error, but the deck is NOT yet Locked: it must keep pulling in (use the tight enter
        // tolerance), so it does not falsely report Locked before it has settled inside the enter zone.
        double errorBeats = (Settings.LockToleranceBeats + Settings.ExitLockToleranceBeats) / 2.0;
        DeckPhase master = At(errorBeats * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(
            slave, master, beatmatchedRate: 1.0, Settings, previousState: SyncLockState.Active);

        Assert.Equal(SyncLockState.Active, result.State);
        Assert.Equal(1.0 + (errorBeats * Settings.Gain), result.EffectiveRate, precision: 9);
    }

    [Fact]
    public void BeyondExitZone_WhenAlreadyLocked_BreaksLockToActive()
    {
        // Past the exit tolerance even a Locked deck must release and correct — hysteresis widens the zone,
        // it does not weld the deck Locked forever.
        double errorBeats = Settings.ExitLockToleranceBeats + 0.01;
        DeckPhase master = At(errorBeats * BeatSeconds);
        DeckPhase slave = At(0.0);

        PhaseLockCorrection result = PhaseLockController.Correct(
            slave, master, beatmatchedRate: 1.0, Settings, previousState: SyncLockState.Locked);

        Assert.Equal(SyncLockState.Active, result.State);
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

    [Fact]
    public void FromATenthOfABeat_SettlesUnderTwoMilliseconds_WithinTwentySeconds_WithoutOvershoot()
    {
        // Integrate a 125 BPM pair at a 60 Hz correction tick. The error decays with τ = 60 / (bpm · Gain),
        // 6 s at the default gain, so 0.1 → 0.004 beat (≈ 1.9 ms, ln 25 ≈ 3.2 τ) lands at ~19.3 s.
        const double bpm = 125.0;
        const double tick = 1.0 / 60.0;
        double masterSeconds = 0.1 * 60.0 / bpm;
        double slaveSeconds = 0.0;
        SyncLockState state = SyncLockState.Active;
        double previousError = double.MaxValue;
        double? settledAt = null;
        bool wasLocked = false;

        for (int n = 0; n < 60 * 60; n++)
        {
            PhaseLockCorrection c = PhaseLockController.Correct(
                new DeckPhase(slaveSeconds, 0.0, bpm), new DeckPhase(masterSeconds, 0.0, bpm), 1.0, Settings, state);

            // Monotone and never across zero: no overshoot, no oscillation.
            Assert.InRange(c.ErrorBeats, double.Epsilon, previousError);
            if (wasLocked)
                Assert.Equal(SyncLockState.Locked, c.State);
            settledAt ??= c.ErrorBeats < 0.004 ? n * tick : null;

            previousError = c.ErrorBeats;
            wasLocked = c.State == SyncLockState.Locked;
            state = c.State;
            masterSeconds += tick;
            slaveSeconds += c.EffectiveRate * tick;
        }

        Assert.NotNull(settledAt);
        Assert.True(settledAt <= 20.0, $"settled at {settledAt:F1} s");
        Assert.Equal(SyncLockState.Locked, state);
    }
}
