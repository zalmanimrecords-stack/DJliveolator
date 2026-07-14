namespace Liveolator.Core.Beat;

/// <summary>
/// Whether two decks' audible tempos are beatmatched — the test behind the per-deck BPM readout's
/// "matched" highlight (doc 11). Pure so the UI can colour both counters green the instant the DJ dials
/// the pitch faders together, without any engine round-trip.
/// </summary>
public static class BpmMatch
{
    /// <summary>The default beatmatch window (BPM). A tenth keeps the highlight honest — it lights only
    /// when the tempos are genuinely locked, not merely close.</summary>
    public const double DefaultToleranceBpm = 0.1;

    // √2 is the geometric midpoint of an octave: fold bpmB by ×2 / ÷2 until it sits within a √2 factor of
    // bpmA and it lands in bpmA's octave — the same fold SYNC applies (TempoSyncCalculator). This makes a
    // half/double-time lock (a 140 leader with a 70 follower) read as matched, matching pro-app behavior.
    private static readonly double UpperFold = Math.Sqrt(2.0);
    private static readonly double LowerFold = Math.Sqrt(0.5);

    /// <summary>
    /// True when both tempos are real (finite and positive) and beatmatched — either at unison or at an
    /// octave (half/double time). <paramref name="toleranceBpm"/> is applied after folding <paramref name="bpmB"/>
    /// into <paramref name="bpmA"/>'s octave, so the window is a genuine tempo lock, not merely close. A
    /// non-positive tempo means "no track / un-analyzed", which never matches — so two empty decks don't
    /// read as locked.
    /// </summary>
    public static bool AreMatched(double bpmA, double bpmB, double toleranceBpm = DefaultToleranceBpm)
    {
        if (!double.IsFinite(bpmA) || !double.IsFinite(bpmB) || bpmA <= 0.0 || bpmB <= 0.0)
            return false;

        double folded = bpmB;
        while (folded / bpmA < LowerFold)
            folded *= 2.0;
        while (folded / bpmA >= UpperFold)
            folded /= 2.0;
        return Math.Abs(bpmA - folded) <= toleranceBpm;
    }

    /// <summary>
    /// The octave factor relating <paramref name="bpm"/> to <paramref name="referenceBpm"/> when the two are
    /// octave-matched — the nearest power of two to <c>bpm / referenceBpm</c>, folded by the same √2 boundary
    /// <see cref="AreMatched"/> uses. <c>1</c> = unison, <c>0.5</c> = this deck runs at half-time,
    /// <c>2</c> = double-time (and so on for deeper octaves). Returns <c>1</c> (unison → no badge) when
    /// either tempo is non-positive or non-finite. Lets the UI tag a half/double-time lock without
    /// re-deriving the fold or coupling the two decks.
    /// </summary>
    public static double OctaveFactor(double bpm, double referenceBpm)
    {
        if (!double.IsFinite(bpm) || !double.IsFinite(referenceBpm) || bpm <= 0.0 || referenceBpm <= 0.0)
            return 1.0;

        double folded = bpm / referenceBpm;
        double factor = 1.0;
        while (folded < LowerFold) { folded *= 2.0; factor *= 0.5; }
        while (folded >= UpperFold) { folded /= 2.0; factor *= 2.0; }
        return factor;
    }
}
