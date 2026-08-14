namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Where the beat actually falls, as a phase within one beat period, plus how much to trust it.
/// </summary>
/// <param name="PhaseSeconds">Offset of the beat inside the period, in [0, beatPeriod). A time t is on
/// the beat when <c>t mod beatPeriod == PhaseSeconds</c>.</param>
/// <param name="Confidence">0..1. How much better the winning phase explains the onsets than chance
/// would — 0 means the onsets carry no usable phase and the caller must not align on this.</param>
/// <param name="Inliers">Onsets that landed on the winning phase within the tolerance.</param>
/// <param name="Total">Onsets considered.</param>
public readonly record struct KickPhase(double PhaseSeconds, double Confidence, int Inliers, int Total)
{
    /// <summary>No usable phase — too few onsets, or they carry no periodic structure.</summary>
    public static KickPhase None { get; } = new(0.0, 0.0, 0, 0);
}

/// <summary>
/// Recovers the BEAT PHASE from a track's detected onsets, so two tracks can be aligned on where their
/// kicks really land rather than on a stored downbeat that may sit anywhere.
/// <para>Measured motivation: on a real set the declared downbeat was ~165 ms off the audio — 0.38 of a
/// beat — which is heard as a flam through the whole crossfade even though the tempo match and the bar
/// arithmetic were both exact.</para>
/// <para><b>Why this is not a circular mean.</b> The persisted onset list is not clean kicks: on real
/// psytrance the circular concentration of its phases measured 0.043, i.e. all but uniform, because the
/// list is dominated by non-kick transients. Averaging that is averaging noise. Instead every onset's own
/// phase is tried as a hypothesis, scored by how many OTHER onsets agree with it within a tolerance, and
/// only the agreeing ones are then averaged. Junk spreads out and cannot win; a real kick pattern can.
/// </para>
/// <para>This estimates BEAT phase, not which beat is beat 1. Beat phase is what removes the flam; bar
/// alignment is a separate question.</para>
/// </summary>
public static class KickPhaseEstimator
{
    /// <summary>How close an onset must sit to a hypothesis to count as agreeing with it. 25 ms is under
    /// a tenth of a beat at dance tempos — tight enough that a wrong phase cannot collect inliers, wide
    /// enough to absorb detector jitter on a real kick.</summary>
    public const double DefaultToleranceSeconds = 0.025;

    /// <summary>Fewer onsets than this cannot establish a phase, so none is reported.</summary>
    public const int MinimumOnsets = 8;

    /// <summary>
    /// The beat phase best supported by <paramref name="onsetSeconds"/> at <paramref name="bpm"/>.
    /// Returns <see cref="KickPhase.None"/> when there is too little to go on — the caller should then
    /// fall back rather than align on a guess.
    /// </summary>
    public static KickPhase Estimate(
        IReadOnlyList<double>? onsetSeconds,
        double bpm,
        double toleranceSeconds = DefaultToleranceSeconds)
    {
        if (onsetSeconds is null || bpm <= 0.0 || toleranceSeconds <= 0.0)
            return KickPhase.None;

        double period = 60.0 / bpm;
        if (toleranceSeconds >= period / 2.0)
            throw new ArgumentOutOfRangeException(
                nameof(toleranceSeconds),
                toleranceSeconds,
                "Tolerance must be under half a beat, or every onset agrees with every hypothesis.");

        // Reduce to phases once; only the position within the beat matters.
        var phases = new List<double>(onsetSeconds.Count);
        foreach (double t in onsetSeconds)
        {
            if (double.IsFinite(t) && t >= 0.0)
                phases.Add(Wrap(t, period));
        }

        if (phases.Count < MinimumOnsets)
            return KickPhase.None;

        // Each onset's own phase is a hypothesis. Scoring by agreement is what makes this robust: a
        // hypothesis sitting on junk collects only the junk that happens to be near it.
        double bestPhase = 0.0;
        int bestInliers = 0;
        foreach (double candidate in phases)
        {
            int inliers = 0;
            foreach (double p in phases)
            {
                if (Math.Abs(SignedOffset(p, candidate, period)) <= toleranceSeconds)
                    inliers++;
            }

            if (inliers > bestInliers)
            {
                bestInliers = inliers;
                bestPhase = candidate;
            }
        }

        // Refine on the winners only, so the estimate is not dragged by the onsets it already rejected.
        double sum = 0.0;
        int counted = 0;
        foreach (double p in phases)
        {
            double offset = SignedOffset(p, bestPhase, period);
            if (Math.Abs(offset) <= toleranceSeconds)
            {
                sum += offset;
                counted++;
            }
        }

        double refined = counted > 0 ? Wrap(bestPhase + (sum / counted), period) : bestPhase;

        // A hypothesis catches 2*tolerance/period of the onsets by chance alone; confidence is how far
        // past that the winner got, so a uniform (structureless) list scores ~0 however long it is.
        double chance = Math.Min(1.0, 2.0 * toleranceSeconds / period);
        double share = bestInliers / (double)phases.Count;
        double confidence = chance >= 1.0 ? 0.0 : Math.Clamp((share - chance) / (1.0 - chance), 0.0, 1.0);

        return new KickPhase(refined, confidence, bestInliers, phases.Count);
    }

    /// <summary>
    /// The instant nearest <paramref name="seconds"/> that sits on <paramref name="phaseSeconds"/>. Never
    /// moves by more than half a beat, so an anchor is corrected rather than relocated.
    /// </summary>
    public static double SnapToPhase(double seconds, double phaseSeconds, double bpm)
    {
        if (bpm <= 0.0)
            return seconds;

        double period = 60.0 / bpm;
        double snapped = seconds - SignedOffset(Wrap(seconds, period), Wrap(phaseSeconds, period), period);
        return snapped < 0.0 ? snapped + period : snapped;
    }

    private static double Wrap(double value, double period)
    {
        double m = value % period;
        return m < 0.0 ? m + period : m;
    }

    // Distance from `phase` to `reference` on the circle, in (-period/2, period/2].
    private static double SignedOffset(double phase, double reference, double period)
    {
        double d = (phase - reference) % period;
        if (d > period / 2.0)
            d -= period;
        else if (d < -period / 2.0)
            d += period;
        return d;
    }
}
