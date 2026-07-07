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
    // 2.5x rescues the fast-tempo trap where the strongest in-band lag is the 2.5-beat sub-harmonic
    // (174 BPM reads as ~69.6): the beat lag itself is nearly as strong there, so this promotion demands
    // near-parity evidence — mere subdivision energy must never push a genuinely slow track fast.
    private const double TwoAndAHalfTimeEvidenceRatio = 0.5;
    private static readonly (double Factor, double EvidenceRatio)[] FastTempoPromotions =
        { (2.0, DoubleTimeEvidenceRatio), (2.5, TwoAndAHalfTimeEvidenceRatio) };
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

        bestLag = PreferSupportedFastTempo(
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

    // When the winning lag is slow (< 100 BPM), check whether a supported fast multiple (2x, 2.5x) of it
    // is the real tempo: dense fast onset trains (DnB half-time) make the 2-beat and 2.5-beat sub-harmonics
    // the strongest lags, folding 174 down to ~87 / ~70. Each factor promotes only when the fast lag holds
    // its own autocorrelation evidence; among qualifying factors the strongest evidence wins.
    private int PreferSupportedFastTempo(
        int bestLag,
        double bestValue,
        IReadOnlyList<double> correlations,
        int minLag,
        int maxLag,
        double envelopeRateHz)
    {
        double selectedBpm = 60.0 * envelopeRateHz / bestLag;
        if (selectedBpm >= DoubleTimeCeilingBpm || bestValue <= 0.0)
            return bestLag;

        int promotedLag = bestLag;
        double promotedValue = double.NegativeInfinity;
        foreach ((double factor, double evidenceRatio) in FastTempoPromotions)
        {
            if (selectedBpm * factor > _maxBpm)
                continue;

            int target = (int)Math.Round(bestLag / factor);
            int from = Math.Max(minLag, target - HarmonicSearchRadius);
            int to = Math.Min(maxLag, target + HarmonicSearchRadius);
            double strongest = double.NegativeInfinity;
            int strongestLag = target;
            for (int lag = from; lag <= to; lag++)
            {
                if (correlations[lag] > strongest)
                {
                    strongest = correlations[lag];
                    strongestLag = lag;
                }
            }

            if (strongest >= bestValue * evidenceRatio && strongest > promotedValue)
            {
                promotedValue = strongest;
                promotedLag = strongestLag;
            }
        }

        return promotedLag;
    }
}

/// <summary>Result of tempo estimation: BPM plus a 0..1 confidence.</summary>
public readonly record struct TempoEstimate(double Bpm, double Confidence);
