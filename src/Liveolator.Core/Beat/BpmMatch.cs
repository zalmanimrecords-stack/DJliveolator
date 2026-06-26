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

    /// <summary>
    /// True when both tempos are real (finite and positive) and differ by no more than
    /// <paramref name="toleranceBpm"/>. A non-positive tempo means "no track / un-analyzed", which never
    /// matches — so two empty decks don't read as locked.
    /// </summary>
    public static bool AreMatched(double bpmA, double bpmB, double toleranceBpm = DefaultToleranceBpm)
    {
        if (!double.IsFinite(bpmA) || !double.IsFinite(bpmB) || bpmA <= 0.0 || bpmB <= 0.0)
            return false;
        return Math.Abs(bpmA - bpmB) <= toleranceBpm;
    }
}
