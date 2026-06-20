namespace Liveolator.Core.Analysis.Bpm;

/// <summary>A refined beat grid: a sub-BPM-resolution tempo, a continuous first-beat phase, and the
/// onset-phase coherence (0..1) of the fit — how tightly the detected kicks land on the grid.</summary>
public readonly record struct GridFit(double Bpm, double FirstBeatSeconds, double Coherence);

/// <summary>
/// Refines a coarse tempo estimate so the beat grid lands on the actual kicks. The autocorrelation
/// <see cref="TempoEstimator"/> reports tempo at integer envelope-frame resolution (e.g. 139.67 BPM for a
/// true 140 — lag 37 is the nearest frame, 140 is literally unreachable), so a uniform grid built from it
/// drifts off the kicks by ~a beat across a long track. This stage peak-picks the kick-onset envelope and
/// fits a sub-frame tempo + continuous phase by maximising onset-phase coherence (a circular-statistics
/// comb fit: the phase is closed-form per period, so only the period is searched). It also reconciles
/// octave / 3:2 metrical confusions against a target tempo band (coherence picks the metrically-correct
/// candidate; the band rejects exact octaves), and optionally snaps to a clean integer tempo when doing
/// so does not reduce coherence. Pure and hardware-free (doc 16); runs on the offline analysis path only.
/// </summary>
public sealed class GridRefiner
{
    /// <summary>Coherence floor for the caller to trust a fit — below this the kick structure is too weak
    /// (ambient/no-kick material) and the coarse estimate should stand.</summary>
    public const double AcceptCoherence = 0.15;

    private const double SearchRadiusBpm = 2.5;   // covers the integer-lag error (139.67 → 140) and a little
    private const double SearchStepBpm = 0.01;    // sub-BPM resolution the integer lag can't reach
    private const double RelativePeakThreshold = 0.15; // onsets must exceed this fraction of the loudest kick
    private const double SnapToleranceBpm = 0.08;  // snap to a clean integer only within this distance…
    private const double SnapCoherenceMargin = 0.01; // …and only if coherence does not drop more than this
    private const int MinOnsets = 4;

    private readonly double _minBpm;
    private readonly double _maxBpm;

    /// <summary>The target tempo band used for octave/metrical reconciliation. The default 84–168 makes
    /// 140-ish electronic tempos their own home and rejects their exact half/double (70 / 280).</summary>
    public GridRefiner(double minBpm = 84.0, double maxBpm = 168.0)
    {
        if (minBpm <= 0 || maxBpm <= minBpm)
            throw new ArgumentException("Require 0 < minBpm < maxBpm.");
        _minBpm = minBpm;
        _maxBpm = maxBpm;
    }

    /// <summary>
    /// Refines <paramref name="coarseBpm"/> / <paramref name="coarseFirstBeatSeconds"/> against the kick
    /// envelope. Returns the coarse values with zero coherence when the envelope has too few kicks to fit
    /// (so the caller falls back), never throwing.
    /// </summary>
    public GridFit Refine(
        double[] kickEnvelope, double envelopeRateHz, double coarseBpm, double coarseFirstBeatSeconds)
    {
        ArgumentNullException.ThrowIfNull(kickEnvelope);
        var fallback = new GridFit(coarseBpm, coarseFirstBeatSeconds, 0.0);
        if (kickEnvelope.Length < MinOnsets || envelopeRateHz <= 0 || coarseBpm <= 0)
            return fallback;

        (double[] times, double[] weights) = PickOnsets(kickEnvelope, envelopeRateHz);
        if (times.Length < MinOnsets)
            return fallback;

        double totalWeight = 0.0;
        foreach (double w in weights) totalWeight += w;
        if (totalWeight <= 0.0)
            return fallback;

        double best = -1.0, bestBpm = coarseBpm, bestOffset = coarseFirstBeatSeconds;
        foreach (double candidate in CandidateTempos(coarseBpm))
        {
            for (double bpm = candidate - SearchRadiusBpm; bpm <= candidate + SearchRadiusBpm; bpm += SearchStepBpm)
            {
                if (bpm <= 0)
                    continue;
                (double coherence, double offset) = Score(bpm, times, weights, totalWeight);
                // Tie-break toward the candidate closest to the coarse estimate — only matters for the rare
                // genuine 3:2 ambiguity; exact octaves are already removed by the band.
                if (coherence > best + 1e-9 ||
                    (coherence > best - 1e-9 && Math.Abs(bpm - coarseBpm) < Math.Abs(bestBpm - coarseBpm)))
                {
                    best = coherence;
                    bestBpm = bpm;
                    bestOffset = offset;
                }
            }
        }

        // Snap to a clean integer tempo when it's a hair away and coherence holds (140.01 → 140.00),
        // but never when snapping would actually loosen the fit (a genuinely non-integer tempo).
        double nearest = Math.Round(bestBpm);
        if (nearest >= _minBpm && nearest <= _maxBpm && Math.Abs(bestBpm - nearest) <= SnapToleranceBpm)
        {
            (double snapCoherence, double snapOffset) = Score(nearest, times, weights, totalWeight);
            if (snapCoherence >= best - SnapCoherenceMargin)
            {
                bestBpm = nearest;
                bestOffset = snapOffset;
                best = snapCoherence;
            }
        }

        return new GridFit(bestBpm, bestOffset, best);
    }

    // The coarse tempo plus its octave / 3:2 metrical relatives, kept inside the target band. Folding the
    // coarse value into the band guarantees at least one candidate even when the coarse estimate is octave-off.
    private IEnumerable<double> CandidateTempos(double coarseBpm)
    {
        var seen = new HashSet<long>();
        foreach (double factor in new[] { 1.0, 2.0, 0.5, 1.5, 2.0 / 3.0 })
        {
            double bpm = coarseBpm * factor;
            if (bpm >= _minBpm && bpm <= _maxBpm && seen.Add((long)Math.Round(bpm * 10)))
                yield return bpm;
        }

        double folded = coarseBpm;
        while (folded < _minBpm) folded *= 2;
        while (folded > _maxBpm) folded /= 2;
        if (seen.Add((long)Math.Round(folded * 10)))
            yield return folded;
    }

    // Onset-phase coherence at a trial tempo: the normalised resultant of the weighted kick phases. 1.0 =
    // every kick lands on the same grid phase; the resultant's angle gives the best first-beat offset.
    private static (double Coherence, double OffsetSeconds) Score(
        double bpm, double[] times, double[] weights, double totalWeight)
    {
        double period = 60.0 / bpm;
        double re = 0.0, im = 0.0;
        for (int i = 0; i < times.Length; i++)
        {
            double theta = 2.0 * Math.PI * times[i] / period;
            re += weights[i] * Math.Cos(theta);
            im += weights[i] * Math.Sin(theta);
        }

        double coherence = Math.Sqrt(re * re + im * im) / totalWeight;
        double frac = Math.Atan2(im, re) / (2.0 * Math.PI);
        frac -= Math.Floor(frac); // wrap to [0,1)
        return (coherence, frac * period);
    }

    // Local maxima of the kick envelope above a fraction of the loudest kick — the discrete kick hits to
    // fit against, ignoring the noise floor (far cheaper and more robust than summing every frame).
    private static (double[] Times, double[] Weights) PickOnsets(double[] envelope, double rateHz)
    {
        double max = 0.0;
        foreach (double v in envelope) if (v > max) max = v;
        if (max <= 0.0)
            return (Array.Empty<double>(), Array.Empty<double>());

        double threshold = max * RelativePeakThreshold;
        var times = new List<double>();
        var weights = new List<double>();
        for (int i = 1; i < envelope.Length - 1; i++)
        {
            double v = envelope[i];
            if (v > threshold && v > envelope[i - 1] && v >= envelope[i + 1])
            {
                times.Add(i / rateHz);
                weights.Add(v);
            }
        }

        return (times.ToArray(), weights.ToArray());
    }
}
