namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Estimates tempo (BPM) from an onset envelope via autocorrelation: the lag with the
/// strongest periodicity inside the searched BPM band gives the tempo. Second stage of the
/// BPM pipeline (doc 03 / doc 16).
/// </summary>
public sealed class TempoEstimator
{
    private const double DoubleTimeCeilingBpm = 100.0;
    private const double DoubleTimeEvidenceRatio = 0.2;
    private const int HarmonicSearchRadius = 2;

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

        var correlations = new double[maxLag + 1];
        double bestVal = double.NegativeInfinity;
        int bestLag = minLag;
        double sum = 0;
        int count = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double acc = 0;
            for (int i = lag; i < n; i++)
                acc += x[i] * x[i - lag];

            // Normalize by the overlap count (n - lag): a raw sum has fewer terms at longer lags, so
            // without this it is systematically smaller for slow tempos and the search is biased toward
            // shorter lags / faster BPM (doc 27 medium fix). The unbiased per-lag mean removes that tilt.
            double normAcc = acc / (n - lag);
            correlations[lag] = normAcc;
            sum += normAcc;
            count++;
            if (normAcc > bestVal)
            {
                bestVal = normAcc;
                bestLag = lag;
            }
        }

        bestLag = PreferSupportedDoubleTime(
            bestLag, bestVal, correlations, minLag, maxLag, envelopeRateHz);
        bestVal = correlations[bestLag];
        double bpm = 60.0 * envelopeRateHz / bestLag;
        // Confidence compares the winning lag's strength to the average, scaled by the signal's variance
        // (zero-lag energy as a per-sample mean) so it stays on the same normalized footing as the lags.
        double variance = zeroLag / n;
        double meanAcc = count > 0 ? sum / count : 0;
        double confidence = variance > 0
            ? Math.Clamp((bestVal - meanAcc) / variance, 0.0, 1.0)
            : 0.0;
        return new TempoEstimate(bpm, confidence);
    }

    private int PreferSupportedDoubleTime(
        int bestLag,
        double bestValue,
        IReadOnlyList<double> correlations,
        int minLag,
        int maxLag,
        double envelopeRateHz)
    {
        double selectedBpm = 60.0 * envelopeRateHz / bestLag;
        double doubleTimeBpm = selectedBpm * 2.0;
        if (selectedBpm >= DoubleTimeCeilingBpm || doubleTimeBpm > _maxBpm || bestValue <= 0.0)
            return bestLag;

        int halfLag = (int)Math.Round(bestLag / 2.0);
        double strongestDoubleTime = double.NegativeInfinity;
        int doubleTimeLag = halfLag;
        int from = Math.Max(minLag, halfLag - HarmonicSearchRadius);
        int to = Math.Min(maxLag, halfLag + HarmonicSearchRadius);
        for (int lag = from; lag <= to; lag++)
        {
            if (correlations[lag] > strongestDoubleTime)
            {
                strongestDoubleTime = correlations[lag];
                doubleTimeLag = lag;
            }
        }

        return strongestDoubleTime >= bestValue * DoubleTimeEvidenceRatio
            ? doubleTimeLag
            : bestLag;
    }
}

/// <summary>Result of tempo estimation: BPM plus a 0..1 confidence.</summary>
public readonly record struct TempoEstimate(double Bpm, double Confidence);
