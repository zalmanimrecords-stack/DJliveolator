namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// The analysed kick strikes a beat display may draw as four-on-the-floor kicks: the real strike times, never
/// the beat grid, and only when <see cref="KickPhaseGate"/> vouches that they are the on-beat kick.
/// <para><b>Why the gate.</b> <see cref="BpmDetector"/> stores the ±25 ms on-phase-filtered low-band list
/// exactly when that gate passes; otherwise the list is raw picks that can hold the off-beat. No proof ⇒ no
/// kicks (owner decision), so a comb never shows a confident wrong kick.</para>
/// <para><b>Why the smoothing.</b> A stored strike is a whole analysis frame (11.6 ms at 44.1 kHz / hop 512,
/// <see cref="KickOnsetPicker"/>), so it sits up to ±5.8 ms off the true kick — enough to make two locked
/// tracks' kicks look split. Each kick is moved by the robust consensus of its neighbours: every neighbour's
/// offset from the beat lattice seen from this kick, <c>d − round(d / beat)·beat</c>. Frame errors
/// decorrelate from kick to kick unless a beat is nearly a whole number of frames; measured at 125 BPM the
/// worst error drops from 6.1 ms to 1.7 ms. Working in beat multiples carries the lattice across breakdowns.</para>
/// </summary>
public static class FourOnTheFloorKicks
{
    // ±8 kicks ≈ 8 s at 125 BPM. Measured on frame-quantised lattices from 120 to 174 BPM, ±4 left up to
    // 3.4 ms while ±8 held 1.9 ms — except near 126 BPM, where a beat is 41.02 frames and the frame error
    // barely changes for dozens of kicks (4.7 ms; no window this size can see it). A wider window only
    // helps while the tempo holds, which the gate's cross-window phase check vouches for.
    private const int NeighbourRadius = 8;
    private const int WindowSize = (2 * NeighbourRadius) + 1;

    // The gate numbers only vouch for the list they were measured with, at the tempo they were measured at: a
    // manual tempo edit keeps both, and they are persisted rounded, so a margin just under the gate can store
    // the raw picks yet pass here. A vouched strike sits within the filter's ±25 ms of the published phase
    // (+0.5 ms catalog rounding), so two of them are never further than this from a whole number of beats.
    private const double MaxPairOffsetSeconds = (2 * KickPhaseEstimator.DefaultToleranceSeconds) + 0.001;

    /// <summary>
    /// The kick strike times (seconds, ascending, same count as analysed) with frame quantisation smoothed
    /// out, or empty when <paramref name="result"/> has no tempo, no kicks, or no on-beat proof — including a
    /// list that is not on its stored tempo's beat lattice. Allocates; call once per loaded track, not per frame.
    /// </summary>
    public static IReadOnlyList<double> From(BpmResult? result)
    {
        if (result is not { Bpm: > 0.0, KickOnsetsSeconds: { Count: > 0 } kicks }
            || !KickPhaseGate.Passes(result.KickPhaseMarginRatio, result.PhaseWindowDisagreementSeconds))
            return Array.Empty<double>();

        double beat = 60.0 / result.Bpm;
        var smoothed = new double[kicks.Count];
        var offsets = new List<double>(WindowSize);
        for (int i = 0; i < kicks.Count; i++)
        {
            // Slide the window inward at the ends so the first and last kicks get a full consensus too.
            int from = Math.Max(0, Math.Min(i - NeighbourRadius, kicks.Count - WindowSize));
            int to = Math.Min(kicks.Count, from + WindowSize);
            offsets.Clear();
            for (int j = from; j < to; j++)
            {
                double d = kicks[j] - kicks[i];
                double offset = d - (Math.Round(d / beat) * beat);
                if (Math.Abs(offset) > MaxPairOffsetSeconds)
                    return Array.Empty<double>();
                offsets.Add(offset);
            }

            smoothed[i] = Math.Max(0.0, kicks[i] + TrimmedMean(offsets));
        }

        return smoothed;
    }

    // Drop the single lowest and highest offset before averaging. The stored list may still hold a flam up to
    // 25 ms off the lattice: trimmed, it cannot drag its neighbours, and it is itself pulled back onto them.
    // A plain mean is the most precise estimator on pure frame noise, which a median is not.
    private static double TrimmedMean(List<double> offsets)
    {
        if (offsets.Count <= 2)
            return offsets.Average();

        offsets.Sort();
        double sum = 0.0;
        for (int k = 1; k < offsets.Count - 1; k++)
            sum += offsets[k];
        return sum / (offsets.Count - 2);
    }
}
