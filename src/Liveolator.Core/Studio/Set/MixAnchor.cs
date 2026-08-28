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
/// <param name="SectionLabel">The structure label this point sits on; null when the fallback rule chose it,
/// and also when a kick advance moved it off the section that did.</param>
/// <param name="Source">Whether structure or the fallback rule chose it. Stays
/// <see cref="AnchorSource.Structure"/> across a kick advance: the section still chose the region, and
/// reporting Fallback there inverted the trust signal on every well-executed long-blend entry.</param>
public sealed record MixAnchor(double SourceSeconds, string? SectionLabel, AnchorSource Source);
