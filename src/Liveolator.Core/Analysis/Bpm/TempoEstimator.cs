namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Estimates tempo (BPM) from an onset envelope via autocorrelation: the lag with the
/// strongest periodicity inside the searched BPM band gives the tempo. Second stage of the
/// BPM pipeline (doc 03 / doc 16).
/// </summary>
public sealed class TempoEstimator
{
    private readonly double _minBpm;
    private readonly double _maxBpm;

    public TempoEstimator(double minBpm = 70.0, double maxBpm = 180.0)
    {
        if (minBpm <= 0 || maxBpm <= minBpm)
            throw new ArgumentException("Require 0 < minBpm < maxBpm.");
        _minBpm = minBpm;
        _maxBpm = maxBpm;
    }

    /// <summary>
    /// Returns the best tempo in the configured band and a 0..1 confidence (how dominant the
    /// winning lag is over the average autocorrelation). Returns (0,0) when undetectable.
    /// </summary>
    public TempoEstimate Estimate(double[] envelope, double envelopeRateHz)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        int n = envelope.Length;
        if (n < 4 || envelopeRateHz <= 0)
            return new TempoEstimate(0, 0);

        // Zero-mean the envelope so autocorrelation reflects periodicity, not DC.
        double mean = 0;
        for (int i = 0; i < n; i++) mean += envelope[i];
        mean /= n;
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = envelope[i] - mean;

        int minLag = (int)Math.Round(60.0 * envelopeRateHz / _maxBpm);
        int maxLag = (int)Math.Round(60.0 * envelopeRateHz / _minBpm);
        if (minLag < 1) minLag = 1;
        if (maxLag >= n) maxLag = n - 1;
        if (maxLag <= minLag)
            return new TempoEstimate(0, 0);

        double zeroLag = 0;
        for (int i = 0; i < n; i++) zeroLag += x[i] * x[i];

        double bestVal = double.NegativeInfinity;
        int bestLag = minLag;
        double sum = 0;
        int count = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double acc = 0;
            for (int i = lag; i < n; i++)
                acc += x[i] * x[i - lag];
            sum += acc;
            count++;
            if (acc > bestVal)
            {
                bestVal = acc;
                bestLag = lag;
            }
        }

        double bpm = 60.0 * envelopeRateHz / bestLag;
        double meanAcc = count > 0 ? sum / count : 0;
        double confidence = zeroLag > 0
            ? Math.Clamp((bestVal - meanAcc) / zeroLag, 0.0, 1.0)
            : 0.0;
        return new TempoEstimate(bpm, confidence);
    }
}

/// <summary>Result of tempo estimation: BPM plus a 0..1 confidence.</summary>
public readonly record struct TempoEstimate(double Bpm, double Confidence);
