namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// Decides whether a measured beat phase may be PUBLISHED as a track's grid anchor, or must be refused so
/// consumers fall back instead of aligning on a guess.
/// <para><b>Why not confidence.</b> Neither the phase estimator's own confidence nor the grid-fit coherence
/// says whether a phase is the KICK. Measured over an 11-track psytrance set:
/// spearman(<see cref="KickPhase.Confidence"/>, phase error) = −0.164 — the highest-confidence track (0.740)
/// was 180.9 ms wrong while a 0.405 track was right to 10.1 ms; <see cref="BpmResult.GridCoherence"/> did
/// better (−0.555) and was still unusable (coherence 0.641 → 193.8 ms error). This cannot be tuned away: a
/// half-beat error is exactly the case where a tight, populous onset cluster exists at the wrong place, so
/// it scores high <em>by construction</em>. Confidence measures how well a phase explains the onsets, never
/// whether that phase is the kick.</para>
/// <para><b>What does work</b>, with the headroom measured on that set:</para>
/// <list type="number">
/// <item><b>Kick identity.</b> Fold the low-band amplitude onto one beat and compare the winning phase with
/// its antiphase. The kick hump stood 2.83-10.64x above the mean beat level while the strongest competing
/// hump was 0.16-0.85 of it — so the hump ratio is ≥ 1.18 when the phase is right and ≤ 0.85 when it is a
/// half-beat off, on 10 of 11 tracks.</item>
/// <item><b>Cross-window stability.</b> A phase fitted over one mid-file window must agree with the
/// whole-file phase: measured 0.1-5.9 ms on ten tracks and 167.0 ms on the one whose declared tempo is
/// itself wrong, where no single global phase exists.</item>
/// </list>
/// Pure and hardware-free. The thresholds are corpus-calibration knobs and the RAW signals are what get
/// persisted (<see cref="BpmResult.KickPhaseMarginRatio"/>,
/// <see cref="BpmResult.PhaseWindowDisagreementSeconds"/>), so they can be retuned without re-analyzing.
/// </summary>
public static class KickPhaseGate
{
    /// <summary>
    /// Hump ratio (winning phase vs. its antiphase) a phase must reach to be published. The measured
    /// separation is ≥1.18 for a correct phase and ≤0.85 for a half-beat error; this sits between them,
    /// nearer the failing side, because a refusal costs a phase lock while a wrong anchor costs the mix.
    /// </summary>
    public const double MinimumMarginRatio = 1.05;

    /// <summary>How far the mid-file phase may sit from the whole-file phase. 15 ms is an order above the
    /// 0.1-5.9 ms measured on stable tracks and an order below the 167.0 ms of the unstable one.</summary>
    public const double MaximumWindowDisagreementSeconds = 0.015;

    /// <summary>Peak-to-trough of the folded profile, relative to its mean, below which there is no
    /// kick-shaped hump to identify at all (ambient / no-kick material) — measured tracks are &gt; 1.0.</summary>
    public const double MinimumProfileDepth = 0.10;

    /// <summary>Bins in the folded beat profile: ~13 ms at 145 BPM, finer than the 11.6 ms analysis frame
    /// spacing and far finer than a kick hump, so the fold resolves the hump without starving a bin.</summary>
    public const int ProfileBins = 32;

    /// <summary>Where the mid-file stability window starts, as a fraction of the track.</summary>
    public const double WindowStartFraction = 0.4;

    /// <summary>Length of the mid-file stability window (~200 beats at dance tempos).</summary>
    public const double WindowSeconds = 90.0;

    /// <summary>
    /// Whether a phase carrying these signals may be published. Missing evidence is never a pass: a caller
    /// that could not measure the gates has nothing to vouch with, and must fall back.
    /// </summary>
    public static bool Passes(double? marginRatio, double? windowDisagreementSeconds)
        => marginRatio is double margin
           && windowDisagreementSeconds is double disagreement
           && margin >= MinimumMarginRatio
           && disagreement <= MaximumWindowDisagreementSeconds;

    /// <summary>
    /// Beat-synchronous average of a per-frame signal over one beat period — the reference measurement,
    /// because it assumes only the TEMPO and never a phase, so no anchor can leak into it. Returns empty
    /// when the input cannot fill every bin (too short to fold).
    /// </summary>
    public static double[] BeatProfile(
        IReadOnlyList<double>? frames, double frameRateHz, double bpm, int bins = ProfileBins)
    {
        if (frames is null || frames.Count == 0 || frameRateHz <= 0.0 || bpm <= 0.0 || bins < 4)
            return Array.Empty<double>();

        double period = 60.0 / bpm;
        var sum = new double[bins];
        var count = new int[bins];
        for (int f = 0; f < frames.Count; f++)
        {
            int bin = (int)(Wrap(f / frameRateHz, period) / period * bins);
            if (bin >= bins) bin = bins - 1;
            sum[bin] += frames[f];
            count[bin]++;
        }

        var profile = new double[bins];
        for (int b = 0; b < bins; b++)
        {
            // An empty bin would read as a false trough and could fake a margin, so the fold fails instead.
            if (count[b] == 0)
                return Array.Empty<double>();
            profile[b] = sum[b] / count[b];
        }

        return profile;
    }

    /// <summary>
    /// How much the folded low-band hump at <paramref name="phaseSeconds"/> stands above the hump half a
    /// beat away — the half-beat discriminator. Measured above the profile's own floor, so the ratio is of
    /// hump HEIGHTS rather than of absolute levels (a shared baseline compresses the contrast toward 1).
    /// 0 when there is nothing kick-like to identify.
    /// </summary>
    public static double MarginRatio(double[]? profile, double bpm, double phaseSeconds)
    {
        if (profile is null || profile.Length < 4 || bpm <= 0.0)
            return 0.0;

        double period = 60.0 / bpm;
        double floor = profile.Min();
        double range = profile.Max() - floor;
        double mean = profile.Average();
        if (mean <= 0.0 || range / mean < MinimumProfileDepth)
            return 0.0;

        double atPhase = Sample(profile, period, phaseSeconds) - floor;
        double atAntiphase = Sample(profile, period, phaseSeconds + (period / 2.0)) - floor;
        if (atPhase <= 0.0)
            return 0.0;

        // The antiphase can sit exactly on the profile floor (a clean kick with silence between hits), so
        // the denominator is floored at 2% of the hump — the ratio saturates at 50 instead of dividing by 0.
        return atPhase / Math.Max(atAntiphase, range * 0.02);
    }

    /// <summary>
    /// Distance between the phase fitted over one mid-file window and <paramref name="wholePhaseSeconds"/> —
    /// the stability signal. Null when the window holds too few onsets to fit (no evidence, so no pass).
    /// </summary>
    public static double? WindowDisagreementSeconds(
        IReadOnlyList<double>? onsetSeconds,
        double bpm,
        double wholePhaseSeconds,
        double windowStartFraction = WindowStartFraction,
        double windowSeconds = WindowSeconds)
    {
        if (onsetSeconds is null || onsetSeconds.Count == 0 || bpm <= 0.0)
            return null;

        double span = onsetSeconds[^1];
        double start = span * windowStartFraction;
        double end = start + windowSeconds;
        var window = new List<double>();
        foreach (double t in onsetSeconds)
        {
            if (t >= start && t <= end)
                window.Add(t);
        }

        KickPhase fitted = KickPhaseEstimator.Estimate(window, bpm);
        if (fitted.Total < KickPhaseEstimator.MinimumOnsets)
            return null;

        return CircularDistance(fitted.PhaseSeconds, wholePhaseSeconds, 60.0 / bpm);
    }

    // Linear interpolation between bin centres (bin b is centred at (b + 0.5) of a bin width), wrapping
    // around the beat — the profile is a circle.
    private static double Sample(double[] profile, double period, double seconds)
    {
        int bins = profile.Length;
        double position = (Wrap(seconds, period) / period * bins) - 0.5;
        int lower = (int)Math.Floor(position);
        double fraction = position - lower;
        return (profile[Mod(lower, bins)] * (1.0 - fraction)) + (profile[Mod(lower + 1, bins)] * fraction);
    }

    private static double CircularDistance(double a, double b, double period)
    {
        double d = Math.Abs(a - b) % period;
        return Math.Min(d, period - d);
    }

    private static double Wrap(double value, double period)
    {
        double m = value % period;
        return m < 0.0 ? m + period : m;
    }

    private static int Mod(int value, int modulus)
    {
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }
}
