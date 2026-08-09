namespace Liveolator.Core.Studio.Set;

/// <summary>How the two records are joined.</summary>
public enum TransitionType
{
    /// <summary>A full-length blend — the two records play together for a phrase or more.</summary>
    Blend,

    /// <summary>The shortest legal crossfade, used when the runway is tight or a grid is not trustworthy.</summary>
    Short,
}

/// <summary>
/// One join in a built set, as reported back to whoever asked for it. Everything here is something the
/// caller can act on — change a track, widen the warp limit, re-analyze a grid. Positions are timeline
/// seconds so a transition can be auditioned directly.
/// </summary>
public sealed record SetTransition(
    int Index,
    string FromPath,
    string ToPath,
    double StartSeconds,
    double EndSeconds,
    int OverlapBars,
    double OverlapSeconds,
    TransitionType Type,
    MixAnchor OutAnchor,
    MixAnchor InAnchor,
    double TempoBpm,
    double FromWarpPercent,
    double ToWarpPercent,
    string? KeyFrom,
    string? KeyTo,
    string? KeyRelationship,
    bool PhaseLocked,
    double? GridConfidenceFrom,
    double? GridConfidenceTo,
    IReadOnlyList<SetWarning> Warnings);
