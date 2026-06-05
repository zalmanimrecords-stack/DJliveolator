namespace Liveolator.Core.Beat;

/// <summary>
/// Resolves when a quantized launch (visual clip/scene/transition — doc 08; or audio quantize —
/// doc 11) should fire, gating on beat confidence. When the clock is not trustworthy (confidence
/// below the threshold, or no timeline yet), it falls back to firing immediately rather than
/// snapping to a shaky grid — the shared guard both engines use (doc 03/08 risks).
/// </summary>
public static class QuantizedLaunch
{
    /// <summary>Default minimum confidence required to honor a quantized boundary.</summary>
    public const double DefaultMinConfidence = 0.5;

    /// <summary>
    /// Host time at which <paramref name="when"/> should fire on <paramref name="timeline"/>, or
    /// <paramref name="fromHostTimeTicks"/> when firing immediately (Immediate, low confidence, or
    /// no timeline).
    /// </summary>
    public static long ResolveFireTime(
        Quantize when,
        int everyN,
        long fromHostTimeTicks,
        IBeatTimeline? timeline,
        double confidence,
        double minConfidence = DefaultMinConfidence,
        int beatsPerBar = BeatQuantizer.DefaultBeatsPerBar)
    {
        if (when == Quantize.Immediate || timeline is null || confidence < minConfidence)
            return fromHostTimeTicks;

        return BeatQuantizer.ResolveFireTime(when, everyN, fromHostTimeTicks, timeline, beatsPerBar);
    }
}
