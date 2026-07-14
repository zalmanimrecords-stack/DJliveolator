using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class FirstBeatEstimatorTests
{
    private readonly FirstBeatEstimator _estimator = new();

    [Fact]
    public void Estimate_PeaksOnTheBeat_AnchorsNearZero()
    {
        // Envelope rate 100 Hz, 120 BPM => 0.5 s/beat => 50 frames/beat. Spikes on beat boundaries
        // (frames 0, 50, 100…) place the first beat at ~0 s.
        double[] envelope = BeatEnvelope(framesPerBeat: 50, beats: 8, peakFrame: 0);

        double anchor = _estimator.Estimate(envelope, bpm: 120.0, envelopeRateHz: 100.0);

        Assert.InRange(anchor, 0.0, 0.02);
    }

    [Fact]
    public void Estimate_PeaksOffsetWithinTheBeat_RecoversTheOffset()
    {
        // The strongest onsets sit 10 frames into each beat (0.1 s at 100 Hz) — the first-beat anchor.
        double[] envelope = BeatEnvelope(framesPerBeat: 50, beats: 8, peakFrame: 10);

        double anchor = _estimator.Estimate(envelope, bpm: 120.0, envelopeRateHz: 100.0);

        Assert.InRange(anchor, 0.08, 0.12);
    }

    [Fact]
    public void Estimate_AnchorIsAlwaysLessThanOneBeat()
    {
        // The anchor is a within-beat offset, so it must fall inside [0, beatSeconds).
        double[] envelope = BeatEnvelope(framesPerBeat: 50, beats: 8, peakFrame: 30);

        double anchor = _estimator.Estimate(envelope, bpm: 120.0, envelopeRateHz: 100.0);

        Assert.InRange(anchor, 0.0, 60.0 / 120.0);
    }

    [Theory]
    [InlineData(0.0, 100.0)]
    [InlineData(120.0, 0.0)]
    public void Estimate_NonPositiveInputs_ReturnsZero(double bpm, double rate)
    {
        double[] envelope = BeatEnvelope(framesPerBeat: 50, beats: 4, peakFrame: 5);
        Assert.Equal(0.0, _estimator.Estimate(envelope, bpm, rate), precision: 6);
    }

    [Fact]
    public void Estimate_EmptyEnvelope_ReturnsZero()
    {
        Assert.Equal(0.0, _estimator.Estimate(System.Array.Empty<double>(), 120.0, 100.0), precision: 6);
    }

    // Builds an onset envelope with a unit spike at <paramref name="peakFrame"/> within each beat period.
    private static double[] BeatEnvelope(int framesPerBeat, int beats, int peakFrame)
    {
        var env = new double[framesPerBeat * beats];
        for (int b = 0; b < beats; b++)
            env[(b * framesPerBeat) + peakFrame] = 1.0;
        return env;
    }
}
