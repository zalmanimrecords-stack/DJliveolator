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

    private static double[] AccentedBeatEnvelope(
        int frames, int beatPeriod, double strong, double weak)
    {
        var envelope = new double[frames];
        for (int frame = 0, beat = 0; frame < frames; frame += beatPeriod, beat++)
            envelope[frame] = beat % 2 == 0 ? strong : weak;
        return envelope;
    }
}
