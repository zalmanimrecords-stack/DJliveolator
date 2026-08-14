using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

/// <summary>
/// Phase-anchor regression: the first-beat anchor that two-deck sync aligns to must follow the KICK, not
/// whatever broadband transient is loudest. On a four-on-the-floor with a sustained off-beat bass and a
/// bright off-beat hat, a broadband-energy anchor is pulled toward the off-beat; the kick-band anchor stays
/// on the down-beat. See docs/24+ system review (2026-06-27) — anchoring phase on the broadband envelope
/// (BpmDetector) was the dominant cause of unsatisfying beat-sync.
/// </summary>
public sealed class BpmDetectorPhaseAnchorTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void Detect_KickWithOffbeatBroadbandPollution_AnchorsPhaseToTheKick()
    {
        // The off-beat pollution (bass + hats) sits ABOVE the kick crossover, so the kick band stays clean
        // and the only failure mode is the phase SOURCE: a broadband anchor follows the loud off-beat
        // transients, a kick anchor stays on the down-beat. This isolates the P0 phase-source bug.
        const double bpm = 128.0;
        const double kickOffsetSeconds = 0.10;
        float[] signal = BeatMixSignals.KickBassHatsFourOnFloor(
            bpm, SampleRate, seconds: 20.0, kickOffsetSeconds: kickOffsetSeconds, bassHz: 320.0);

        BpmResult result = new BpmDetector().Detect(signal, SampleRate);

        // Guard: the test must fail on PHASE, not because tempo detection collapsed.
        Assert.InRange(result.Bpm, bpm - 3.0, bpm + 3.0);

        double period = 60.0 / result.Bpm;
        double offbeatSeconds = kickOffsetSeconds + period / 2.0;
        double toKick = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, kickOffsetSeconds, period);
        double toOffbeat = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, offbeatSeconds, period);

        Assert.True(
            toKick < 0.03,
            $"first-beat anchor should sit on the kick (offset {kickOffsetSeconds:F3}s); " +
            $"was {result.FirstBeatSeconds:F3}s, distance-to-kick {toKick:F3}s, distance-to-offbeat {toOffbeat:F3}s");
        Assert.True(toKick < toOffbeat, "anchor must be closer to the kick than to the off-beat");
    }

    [Fact]
    public void Detect_KickWithOffbeatSubBassInKickBand_AnchorsPhaseToTheKick()
    {
        // The off-beat bass now sits INSIDE the kick band (110 Hz): a band-only onset cannot tell it from
        // the kick, so its coherence collapses and phase falls back to the polluted broadband. Percussive
        // (HPSS) separation removes the sustained narrow-band bass before the onset stage, so the anchor
        // locks to the kick. This is the regression guard for the secondary cause (P1 stems).
        const double bpm = 128.0;
        const double kickOffsetSeconds = 0.10;
        float[] signal = BeatMixSignals.KickBassHatsFourOnFloor(
            bpm, SampleRate, seconds: 20.0, kickOffsetSeconds: kickOffsetSeconds, bassHz: 110.0);

        BpmResult result = new BpmDetector().Detect(signal, SampleRate);

        Assert.InRange(result.Bpm, bpm - 3.0, bpm + 3.0);
        double period = 60.0 / result.Bpm;
        double offbeatSeconds = kickOffsetSeconds + period / 2.0;
        double toKick = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, kickOffsetSeconds, period);
        double toOffbeat = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, offbeatSeconds, period);
        Assert.True(toKick < 0.03, $"anchor distance-to-kick {toKick:F3}s (to off-beat {toOffbeat:F3}s)");
        Assert.True(toKick < toOffbeat, "anchor must be closer to the kick than to the off-beat");
    }

    [Fact]
    public void PhaseFromPercussiveOnsets_LandsOnTheOffbeat_WhereTheKickBandDoesNot()
    {
        // The measured root cause. The off-beat layer is as percussive as the kick and louder, so HPSS keeps
        // it and the phase estimated from percussive onsets sits half a beat from the kick. Re-picking the
        // SAME estimator's onsets from the low band fixes it — on the real set that moved agreement with an
        // audio-derived reference from 2/11 tracks to 9/11 (within 8.1 ms).
        const double bpm = 145.0;
        const double kickOffsetSeconds = 0.10;
        float[] signal = BeatMixSignals.KickWithLoudOffbeatPercussion(
            bpm, SampleRate, seconds: 60.0, kickOffsetSeconds: kickOffsetSeconds);
        double period = 60.0 / bpm;

        double percussive = PhaseFrom(new PercussiveOnsetEnvelope(), signal, bpm);
        double lowBand = PhaseFrom(new LowBandOnsetEnvelope(), signal, bpm);

        double percussiveToKick = BeatMixSignals.CircularDistanceSeconds(percussive, kickOffsetSeconds, period);
        double lowBandToKick = BeatMixSignals.CircularDistanceSeconds(lowBand, kickOffsetSeconds, period);
        Assert.True(
            percussiveToKick > 0.06,
            $"the percussive envelope should NOT find the kick on this material; percussive was " +
            $"{percussiveToKick * 1000:F1} ms off, kick band {lowBandToKick * 1000:F1} ms off");
        Assert.True(
            lowBandToKick < 0.025,
            $"the kick band must find the kick; kick band was {lowBandToKick * 1000:F1} ms off, " +
            $"percussive {percussiveToKick * 1000:F1} ms off");
    }

    [Fact]
    public void Detect_LoudOffbeatPercussion_PublishesTheKickAnchor_AndVouchesForIt()
    {
        const double bpm = 145.0;
        const double kickOffsetSeconds = 0.10;
        float[] signal = BeatMixSignals.KickWithLoudOffbeatPercussion(
            bpm, SampleRate, seconds: 60.0, kickOffsetSeconds: kickOffsetSeconds);

        BpmResult result = new BpmDetector().Detect(signal, SampleRate);

        Assert.InRange(result.Bpm, bpm - 3.0, bpm + 3.0); // tempo must not move
        double period = 60.0 / result.Bpm;
        double toKick = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, kickOffsetSeconds, period);
        Assert.True(toKick < 0.03, $"published anchor {result.FirstBeatSeconds:F3}s is {toKick * 1000:F1} ms off the kick");

        // The gates vouched for it, so the anchor is offered for phase alignment.
        Assert.NotNull(result.KickPhaseMarginRatio);
        Assert.NotNull(result.PhaseWindowDisagreementSeconds);
        Assert.True(
            KickPhaseGate.Passes(result.KickPhaseMarginRatio, result.PhaseWindowDisagreementSeconds),
            $"margin {result.KickPhaseMarginRatio}, window disagreement {result.PhaseWindowDisagreementSeconds}");
        // The kick strike list comes from the same envelope as the anchor and carries only on-grid strikes,
        // so SET PHASE cannot snap to an entry that sits half a beat off the published grid.
        Assert.NotEmpty(result.KickOnsetsSeconds);
        Assert.All(result.KickOnsetsSeconds, strike =>
        {
            double off = BeatMixSignals.CircularDistanceSeconds(strike, kickOffsetSeconds, period);
            Assert.True(off < 0.03, $"persisted strike {strike:F3}s is {off * 1000:F1} ms off the grid");
        });
    }

    [Fact]
    public void Detect_OffbeatBassInsideTheKickBand_RefusesToVouchForThePhase()
    {
        // The kick band cannot tell a 110 Hz off-beat bass note from the kick, so the low-band phase is not
        // trustworthy here — the kick-identity gate must REFUSE rather than publish it, and analysis falls
        // back to the shipped (HPSS) anchor instead of aligning on a guess.
        float[] signal = BeatMixSignals.KickBassHatsFourOnFloor(
            bpm: 128.0, SampleRate, seconds: 60.0, kickOffsetSeconds: 0.10, bassHz: 110.0);

        BpmResult result = new BpmDetector().Detect(signal, SampleRate);

        Assert.False(
            KickPhaseGate.Passes(result.KickPhaseMarginRatio, result.PhaseWindowDisagreementSeconds),
            $"margin {result.KickPhaseMarginRatio} should not clear {KickPhaseGate.MinimumMarginRatio}");
        Assert.False(GridConfidenceCalculator.Evaluate(result).PhaseSyncReady);
        // Refusal is not a regression: the fallback anchor is exactly what shipped, still on the kick.
        double period = 60.0 / result.Bpm;
        double toKick = BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, 0.10, period);
        Assert.True(toKick < 0.03, $"fallback anchor {toKick * 1000:F1} ms off the kick");
    }

    private static double PhaseFrom(IKickOnsetEnvelope envelope, float[] signal, double bpm)
    {
        double[] flux = envelope.Compute(signal, SampleRate);
        IReadOnlyList<double> onsets = KickOnsetPicker.Pick(
            flux, envelope.EnvelopeRateHz(SampleRate), envelope.AnalysisLatencySeconds(SampleRate));
        return KickPhaseEstimator.Estimate(onsets, bpm).PhaseSeconds;
    }
}
