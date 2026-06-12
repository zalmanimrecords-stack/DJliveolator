namespace Liveolator.Core.Studio;

/// <summary>
/// Where a planned transition is positioned in time relative to the two tracks. Chosen by
/// <c>TransitionDefaults</c> from the available <see cref="Analysis.TrackCues"/>.
/// </summary>
public enum TransitionAnchor
{
    /// <summary>Blend the outgoing track's outro into the incoming track's intro — the musical
    /// ideal. Requires phrase cues (<c>OutroStart</c>/<c>IntroEnd</c>) on both tracks.</summary>
    OutroToIntro,

    /// <summary>Fallback when phrase cues are absent: overlap the tail of the outgoing track with
    /// the head of the incoming one for the transition length (last N beats ↔ first N beats).</summary>
    TailOverlap,
}
