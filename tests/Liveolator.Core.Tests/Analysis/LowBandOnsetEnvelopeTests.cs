using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public sealed class LowBandOnsetEnvelopeTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void Compute_LowFrequencyBursts_ProduceMuchStrongerEnvelopeThanHighFrequency()
    {
        // Same burst pattern at a kick frequency vs a hi-hat frequency: the LR4 low-pass at 200 Hz must
        // let the kick through and reject the hat, so the kick envelope carries far more onset energy.
        float[] kick = BurstTrain(freqHz: 55.0, bpm: 120.0, SampleRate, seconds: 8);
        float[] hat = BurstTrain(freqHz: 10_000.0, bpm: 120.0, SampleRate, seconds: 8);

        var detector = new LowBandOnsetEnvelope();
        double kickEnergy = Sum(detector.Compute(kick, SampleRate));
        double hatEnergy = Sum(detector.Compute(hat, SampleRate));

        Assert.True(
            kickEnergy > hatEnergy * 20.0,
            $"kick band should dominate: kick={kickEnergy:F4}, hat={hatEnergy:F4}");
    }

    [Fact]
    public void Compute_KickBurstTrain_FeedsTempoEstimatorToRecoverTempo()
    {
        // The low-band envelope must be a valid onset signal for the existing tempo pipeline.
        float[] kick = BurstTrain(freqHz: 55.0, bpm: 124.0, SampleRate, seconds: 12);

        var detector = new LowBandOnsetEnvelope();
        double[] envelope = detector.Compute(kick, SampleRate);
        double rate = detector.EnvelopeRateHz(SampleRate);
        TempoEstimate estimate = new TempoEstimator().Estimate(envelope, rate);

        Assert.InRange(estimate.Bpm, 121.0, 127.0);
    }

    [Fact]
    public void Compute_TooShortSignal_ReturnsEmpty()
    {
        var tiny = new float[16];
        Assert.Empty(new LowBandOnsetEnvelope().Compute(tiny, SampleRate));
    }

    [Fact]
    public void Compute_SampleRateBelowCrossover_ReturnsEmpty()
    {
        // Crossover (200 Hz) above Nyquist: the band can't be formed, so degrade to empty rather than
        // designing an unstable filter (same contract as the waveform band split).
        float[] signal = BurstTrain(freqHz: 55.0, bpm: 120.0, sampleRate: 300, seconds: 4);
        Assert.Empty(new LowBandOnsetEnvelope().Compute(signal, 300));
    }

    private static double Sum(double[] values)
    {
        double total = 0.0;
        foreach (double value in values)
            total += value;
        return total;
    }

    /// <summary>Short tone bursts of <paramref name="freqHz"/> on every beat — a kick or hat pattern.</summary>
    private static float[] BurstTrain(
        double freqHz, double bpm, int sampleRate, double seconds,
        double burstSeconds = 0.04, double amplitude = 1.0)
    {
        int total = (int)(sampleRate * seconds);
        var buffer = new float[total];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        int burstLen = (int)(burstSeconds * sampleRate);
        double w = 2.0 * Math.PI * freqHz / sampleRate;
        for (double pos = 0; pos < total; pos += samplesPerBeat)
        {
            int start = (int)pos;
            for (int i = 0; i < burstLen && start + i < total; i++)
                buffer[start + i] = (float)(amplitude * Math.Sin(w * i));
        }
        return buffer;
    }
}
