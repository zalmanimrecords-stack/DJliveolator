using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public sealed class TempoEstimatorTests
{
    private const double EnvelopeRateHz = 100.0;

    [Fact]
    public void Estimate_PrefersDoubleTime_WhenEveryOtherBeatIsAccented()
    {
        // 150 BPM = one beat every 40 envelope frames. Strong accents every two beats can make the
        // 75 BPM autocorrelation peak larger, but the intermediate beats still provide clear evidence
        // for the real tempo.
        double[] envelope = AccentedBeatEnvelope(
            frames: 1_200, beatPeriod: 40, strong: 1.0, weak: 0.35);

        TempoEstimate result = new TempoEstimator().Estimate(envelope, EnvelopeRateHz);

        Assert.InRange(result.Bpm, 147.0, 153.0);
    }

    [Fact]
    public void Estimate_KeepsTrueSlowTempo_WhenNoIntermediateBeatExists()
    {
        // A genuine 75 BPM pulse has no onset halfway between beats, so it must not be doubled.
        double[] envelope = AccentedBeatEnvelope(
            frames: 1_200, beatPeriod: 80, strong: 1.0, weak: 0.0);

        TempoEstimate result = new TempoEstimator().Estimate(envelope, EnvelopeRateHz);

        Assert.InRange(result.Bpm, 73.0, 77.0);
    }

    [Fact]
    public void Estimate_PromotesTwoAndAHalfSubHarmonic_ForFastTempos()
    {
        // 176-BPM trap (the 174-BPM DnB corpus failure): onsets on every half-beat with a strong accent
        // every 5 half-beats (= 2.5 beats), so the strongest in-band lag is the 2.5-beat sub-harmonic
        // (~70 BPM, big-hit alignment) while the true beat lag is nearly as strong. The estimator must
        // promote to the fast tempo instead of reporting ~70.
        const int halfBeat = 17; // beat = 34 frames -> ~176.5 BPM at 100 Hz; 2.5 beats = lag 85 (~70.6)
        var envelope = new double[1_700];
        for (int k = 0; k * halfBeat < envelope.Length; k++)
            envelope[k * halfBeat] = k % 5 == 0 ? 2.0 : 0.5;

        TempoEstimate result = new TempoEstimator().Estimate(envelope, EnvelopeRateHz);

        Assert.True(result.Bpm > 160.0, $"fast tempo must not fold to a slow sub-harmonic, was {result.Bpm:F1}");
    }

    [Fact]
    public void Estimate_KeepsTrueSlowTempo_WhenOnlySubdivisionEnergyExists()
    {
        // A genuine ~70.6 BPM pulse with a modest ghost hit exactly at the 2.5x trap position (0.4 of a
        // beat in): weak evidence there must NOT promote — the 2.5x rescue demands near-parity evidence.
        var envelope = new double[1_700];
        for (int frame = 0; frame < envelope.Length; frame += 85)
        {
            envelope[frame] = 1.0;
            if (frame + 34 < envelope.Length)
                envelope[frame + 34] = 0.4;
        }

        TempoEstimate result = new TempoEstimator().Estimate(envelope, EnvelopeRateHz);

        Assert.InRange(result.Bpm, 68.5, 72.5);
    }

    [Fact]
    public void Estimate_PicksThePromotionByGateMargin_NotRawCorrelation()
    {
        // Both fast-tempo promotions qualify at once: the 2.5x candidate (lag 34) carries MORE raw
        // autocorrelation than the 2x candidate (lag 42), but only barely clears its strict 0.5 gate,
        // while the 2x clears its loose 0.2 gate by ~3x. Ranking by raw correlation would promote to
        // ~176 BPM; ranking by gate-normalized margin must promote to ~143 BPM (the 2x). Envelope =
        // three impulse trains (periods 84 / 34 / 42), amplitudes tuned so the in-band best lag stays 84
        // (~71 BPM) and the two windows hold exactly that relationship (verified numerically against the
        // estimator's own zero-mean, (n-lag)-normalized autocorrelation).
        var envelope = new double[1_680];
        for (int frame = 0; frame < envelope.Length; frame += 84) envelope[frame] += 1.0;
        for (int frame = 0; frame < envelope.Length; frame += 34) envelope[frame] += 0.75;
        for (int frame = 0; frame < envelope.Length; frame += 42) envelope[frame] += 0.42;

        TempoEstimate result = new TempoEstimator().Estimate(envelope, EnvelopeRateHz);

        Assert.InRange(result.Bpm, 139.0, 147.0);
    }

    [Fact]
    public void Estimate_PromotesTheDottedSubHarmonic_ToTheBeat_NotPastIt()
    {
        // Issue #4: a 125 BPM melodic-house record read as 168. The strongest in-band lag was the
        // 1.5-BEAT (dotted) sub-harmonic at ~83 BPM, and the only rescue on offer was a 2x doubling —
        // which landed on 0.75 of a beat, i.e. 4/3 of the true tempo, and passed its lax 0.2 gate on
        // off-beat energy alone. Here the beat lag (48) carries near-parity evidence while the 0.75-beat
        // lag (36) carries only the off-beat layer, so the 1.5x promotion must win and land on ~125.
        var envelope = new double[4_800];
        for (int frame = 0; frame < envelope.Length; frame += 72) envelope[frame] += 0.60; // dotted accent
        for (int frame = 0; frame < envelope.Length; frame += 48) envelope[frame] += 0.80; // the beat
        for (int frame = 0; frame < envelope.Length; frame += 36) envelope[frame] += 0.45; // off-beat layer

        TempoEstimate result = new TempoEstimator().Estimate(envelope, EnvelopeRateHz);

        Assert.InRange(result.Bpm, 121.0, 129.0);
    }

    private static double[] AccentedBeatEnvelope(
        int frames, int beatPeriod, double strong, double weak)
    {
        var envelope = new double[frames];
        for (int frame = 0, beat = 0; frame < frames; frame += beatPeriod, beat++)
            envelope[frame] = beat % 2 == 0 ? strong : weak;
        return envelope;
    }
}
