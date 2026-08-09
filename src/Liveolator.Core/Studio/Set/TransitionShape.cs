namespace Liveolator.Core.Studio.Set;

/// <summary>
/// The planned geometry of one join: where the outgoing track is left, where the incoming track is
/// entered, and how long the two overlap. Source positions only — placing it on the timeline is the
/// arranger's job.
/// </summary>
/// <param name="Out">Mix-out point in the outgoing track's source seconds.</param>
/// <param name="In">Mix-in point in the incoming track's source seconds.</param>
/// <param name="OverlapBars">Crossfade length in bars (always a legal, phrase-integer value).</param>
/// <param name="Warnings">What had to be compromised, if anything.</param>
public sealed record TransitionShape(
    MixAnchor Out,
    MixAnchor In,
    int OverlapBars,
    IReadOnlyList<SetWarning> Warnings);
