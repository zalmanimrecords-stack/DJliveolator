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
    // Free phase per window: a single global phase collapses at the true tempo whenever a track carries a
    // mid-track arrangement edit (psytrance half-bar cuts) or slow drift — real-track regression: Vibe
    // Tribe "Beyond & Beyond" (true 145.0) scored 0.08 globally and fell back to the 143.55 coarse bin,
    // wrecking two-deck sync. Long enough to still resolve tempo (~48 beats), short enough that one edit
    // costs one window.
    private const double WindowSeconds = 20.0;
    // A near-empty window's resultant is ~1 by chance (one onset is always "in phase" with itself); such
    // windows (breakdowns, silence) are excluded from the score entirely rather than inflating it.
    private const int MinOnsetsPerWindow = 4;
    // A tempo and its double fit sparse kicks EQUALLY well (kicks on every other grid beat are still all
    // in phase), separated only by quantization jitter — so coherence differences this small are a tie,
    // not evidence, and the metrical level is decided by occupancy / the coarse estimate instead.
    private const double CoherenceTieMargin = 0.02;
    // Kicks landing on at least this fraction of a candidate's beats prove the kicks ARE that beat (a
    // four-on-the-floor floor); sparser patterns (DnB bar-rate kicks) leave the level to the coarse tempo.
    private const double OccupiedGridFloor = 0.7;

    private readonly double _minBpm;
    private readonly double _maxBpm;

    /// <summary>The target tempo band used for octave/metrical reconciliation. The default 84–180 matches
    /// <see cref="TempoEstimator"/>'s ceiling so fast tempos (DnB 168–180) are representable — the old 168
    /// ceiling folded a correct coarse 174 down to 87. The 84–90 range now has its double in band too, but
    /// the coherence fit still prefers the true tempo (doubling amplifies the onsets' phase jitter) and the
    /// tie-break leans on the coarse estimate.</summary>
    public GridRefiner(double minBpm = 84.0, double maxBpm = 180.0)
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

        // Sweep each metrical candidate to its own best fit, then pick between candidates. Within one
        // sweep coherence decides outright; between candidates a near-tie is metrical ambiguity (a tempo
        // vs its double both fit sparse kicks), resolved by grid occupancy and closeness to the coarse tempo.
        var fits = new List<(double Bpm, double Offset, double Coherence)>();
        foreach (double candidate in CandidateTempos(coarseBpm))
        {
            double cBest = -1.0, cBpm = candidate, cOffset = 0.0;
            for (double bpm = candidate - SearchRadiusBpm; bpm <= candidate + SearchRadiusBpm; bpm += SearchStepBpm)
            {
                if (bpm <= 0)
                    continue;
                (double coherence, double offset) = Score(bpm, times, weights);
                if (coherence > cBest + 1e-9 ||
                    (coherence > cBest - 1e-9 && Math.Abs(bpm - coarseBpm) < Math.Abs(cBpm - coarseBpm)))
                {
                    cBest = coherence;
                    cBpm = bpm;
                    cOffset = offset;
                }
            }
            if (cBest >= 0.0)
                fits.Add((cBpm, cOffset, cBest));
        }

        if (fits.Count == 0)
            return fallback;

        (double bestBpm, double bestOffset, double best) =
            PickMetricalLevel(fits, coarseBpm, times);

        // Snap to a clean integer tempo when it's a hair away and coherence holds (140.01 → 140.00),
        // but never when snapping would actually loosen the fit (a genuinely non-integer tempo).
        double nearest = Math.Round(bestBpm);
        if (nearest >= _minBpm && nearest <= _maxBpm && Math.Abs(bestBpm - nearest) <= SnapToleranceBpm)
        {
            (double snapCoherence, double snapOffset) = Score(nearest, times, weights);
            if (snapCoherence >= best - SnapCoherenceMargin)
            {
                bestBpm = nearest;
                bestOffset = snapOffset;
                best = snapCoherence;
            }
        }

        return new GridFit(bestBpm, bestOffset, best);
    }

    // Chooses the metrical level among the per-candidate best fits. Clear coherence winner → take it.
    // Near-ties (a tempo vs its half/double fitting sparse kicks identically): a candidate whose grid the
    // kicks fully occupy (a kick on ~every beat) is proven by the kicks themselves and wins, slowest
    // first; otherwise the kick band cannot decide the level and the fit closest to the coarse
    // (full-spectrum) estimate stands. Known ceiling: a snare bleeding into the kick band at exactly the
    // half-tempo rate would read as an occupied slow grid — the real-audio corpus is the watchdog there.
    private static (double Bpm, double Offset, double Coherence) PickMetricalLevel(
        List<(double Bpm, double Offset, double Coherence)> fits,
        double coarseBpm,
        double[] onsetTimes)
    {
        double top = fits.Max(f => f.Coherence);

        var contenders = fits.FindAll(f => f.Coherence >= top - CoherenceTieMargin);
        if (contenders.Count == 1)
            return contenders[0];

        (double Bpm, double Offset, double Coherence)? slowestOccupied = null;
        foreach (var fit in contenders)
        {
            if (OccupiedBeatFraction(fit.Bpm, onsetTimes) >= OccupiedGridFloor
                && (slowestOccupied is null || fit.Bpm < slowestOccupied.Value.Bpm))
                slowestOccupied = fit;
        }
        if (slowestOccupied is not null)
            return slowestOccupied.Value;

        return contenders.MinBy(f => Math.Abs(f.Bpm - coarseBpm));
    }

    // The fraction of grid beats (over the onsets' span) that actually receive an onset. A raw
    // onsets-per-beat COUNT ratio is wrong on noisy material: spurious extra onsets push the ratio past
    // 1.0 for ANY slow candidate, so "slowest occupied" crowned tempos the kicks never played. Distinct
    // beat slots can never exceed the beat count, so extra onsets stop counting as occupancy evidence.
    private static double OccupiedBeatFraction(double bpm, double[] onsetTimes)
    {
        if (bpm <= 0 || onsetTimes.Length == 0)
            return 0.0;
        double period = 60.0 / bpm;
        double first = onsetTimes[0];
        int totalBeats = Math.Max(1, (int)Math.Round((onsetTimes[^1] - first) / period));
        var occupiedSlots = new HashSet<long>();
        foreach (double t in onsetTimes)
            occupiedSlots.Add((long)Math.Round((t - first) / period));
        return Math.Min(1.0, occupiedSlots.Count / (double)totalBeats);
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

    // Onset-phase coherence at a trial tempo: the mean normalised resultant of the weighted kick phases,
    // taken per WINDOW (each window free to have its own phase) and at BOTH the beat period and its half.
    //
    //  - Windowed: |sum over windows of each window's resultant magnitude| ≥ the single global resultant
    //    (triangle inequality), and unlike it survives a mid-track phase edit or slow drift — the exact
    //    failure that read a true 145.0 as the 143.55 coarse bin on real psytrance.
    //  - Half-period harmonic: an offbeat bass the HPSS band keeps sits in ANTIPHASE at the beat period
    //    and cancels the kick's resultant there; on the half-beat grid both align, so the harmonic term
    //    restores the contrast at the true tempo without promoting the double (scored per candidate, the
    //    metrical level is still decided by PickMetricalLevel).
    //
    // 1.0 = every onset in phase in every window on both grids; a clean one-spike-per-beat train still
    // scores 1.0, so the AcceptCoherence floor keeps its meaning. The angle of the summed beat-period
    // resultants gives the best single first-beat offset (the heaviest coherent stretch dominates).
    private static (double Coherence, double OffsetSeconds) Score(double bpm, double[] times, double[] weights)
    {
        double period = 60.0 / bpm;
        double resultantSum = 0.0, countedWeight = 0.0;
        double globalRe = 0.0, globalIm = 0.0;
        int i = 0;
        while (i < times.Length)
        {
            double windowEnd = (Math.Floor(times[i] / WindowSeconds) + 1.0) * WindowSeconds;
            double re1 = 0.0, im1 = 0.0, re2 = 0.0, im2 = 0.0, windowWeight = 0.0;
            int count = 0;
            while (i < times.Length && times[i] < windowEnd)
            {
                double theta = 2.0 * Math.PI * times[i] / period;
                double w = weights[i];
                re1 += w * Math.Cos(theta);
                im1 += w * Math.Sin(theta);
                re2 += w * Math.Cos(2.0 * theta);
                im2 += w * Math.Sin(2.0 * theta);
                windowWeight += w;
                count++;
                i++;
            }

            if (count < MinOnsetsPerWindow)
                continue;
            resultantSum += Math.Sqrt(re1 * re1 + im1 * im1) + Math.Sqrt(re2 * re2 + im2 * im2);
            countedWeight += windowWeight;
            globalRe += re1;
            globalIm += im1;
        }

        if (countedWeight <= 0.0)
            return (0.0, 0.0);

        double coherence = resultantSum / (2.0 * countedWeight);
        double frac = Math.Atan2(globalIm, globalRe) / (2.0 * Math.PI);
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
