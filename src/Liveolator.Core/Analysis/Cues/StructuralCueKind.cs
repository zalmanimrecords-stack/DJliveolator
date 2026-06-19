namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// The kind of musical structure point a <see cref="StructuralCueDetector"/> identifies. These map
/// to the hot-cue bank by the auto-cue slot convention (doc 11/16): bank A holds the high-value
/// performance points (start / drop / breakdown / build), bank B holds phrase mix points and the
/// outro. <see cref="TrackStart"/> and <see cref="OutroStart"/> are the always-safe pair placed even
/// at low confidence; the rest are speculative and gated.
/// </summary>
public enum StructuralCueKind
{
    /// <summary>The first audible downbeat — loop-in / mix-in anchor (always-safe).</summary>
    TrackStart,

    /// <summary>End of the intro / start of the first main section before the drop.</summary>
    IntroEnd,

    /// <summary>The first main drop — kick enters and sustains; the primary mix-into point.</summary>
    Drop,

    /// <summary>A breakdown — the kick drops out for several bars while melody remains.</summary>
    Breakdown,

    /// <summary>A build-up — rising energy / risers leading into the next drop.</summary>
    BuildUp,

    /// <summary>A generic phrase boundary (8/16/32 bars) — a clean mix-in/out point.</summary>
    Phrase,

    /// <summary>Start of the outro — where the exit blend begins (always-safe).</summary>
    OutroStart,
}
