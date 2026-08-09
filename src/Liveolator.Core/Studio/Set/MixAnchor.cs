namespace Liveolator.Core.Studio.Set;

/// <summary>Where a mix point came from — the agent reads this to know how much to trust it.</summary>
public enum AnchorSource
{
    /// <summary>Derived from the track's analyzed <see cref="Liveolator.Core.Analysis.Structure.SongStructure"/>.</summary>
    Structure,

    /// <summary>The plain rule (track head / a fixed distance from the tail), used when structure is absent
    /// or failed the trust checks.</summary>
    Fallback,
}

/// <summary>
/// One end of a transition, as a position in its own track's source timeline. Always quantized to that
/// track's phrase grid, which is what keeps two warped clips phrase-aligned for the whole crossfade.
/// </summary>
/// <param name="SourceSeconds">Offset from track start, in source seconds (before any warp).</param>
/// <param name="SectionLabel">The structure label this point sits on, or null when it came from the fallback.</param>
/// <param name="Source">Whether structure or the fallback rule chose it.</param>
public sealed record MixAnchor(double SourceSeconds, string? SectionLabel, AnchorSource Source);
