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

    private static double[] AccentedBeatEnvelope(
        int frames, int beatPeriod, double strong, double weak)
    {
        var envelope = new double[frames];
        for (int frame = 0, beat = 0; frame < frames; frame += beatPeriod, beat++)
            envelope[frame] = beat % 2 == 0 ? strong : weak;
        return envelope;
    }
}
