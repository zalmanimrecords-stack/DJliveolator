namespace Liveolator.Core.Analysis;

/// <summary>
/// Structural cue points used by the deck UI and auto-mix (doc 11/16). Intro Start and Outro
/// End are auto-detectable via silence detection; Intro End / Outro Start need phrase analysis
/// (not yet implemented) and stay null until then.
/// </summary>
public readonly record struct TrackCues(
    TimeSpan? IntroStart,
    TimeSpan? IntroEnd,
    TimeSpan? OutroStart,
    TimeSpan? OutroEnd)
{
    public static TrackCues None => new(null, null, null, null);
}
