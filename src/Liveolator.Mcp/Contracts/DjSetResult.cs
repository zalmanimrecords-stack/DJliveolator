namespace Liveolator.Mcp.Contracts;

/// <summary>One end of a transition: where it sits in its own track and what put it there.</summary>
public sealed record MixAnchorInfo(double SourceSeconds, string? SectionLabel, string Source);

/// <summary>
/// One join in a built set. Everything here is actionable: swap a track, widen the warp limit, re-analyze
/// a grid, or audition the join at <see cref="StartSeconds"/>.
/// </summary>
public sealed record TransitionInfo(
    int Index,
    string FromPath,
    string ToPath,
    string FromTitle,
    string ToTitle,
    double StartSeconds,
    double EndSeconds,
    int OverlapBars,
    double OverlapSeconds,
    string Type,
    MixAnchorInfo OutAnchor,
    MixAnchorInfo InAnchor,
    double TempoBpm,
    double FromWarpPercent,
    double ToWarpPercent,
    string? KeyFrom,
    string? KeyTo,
    string? KeyRelationship,
    bool PhaseLocked,
    double? GridConfidenceFrom,
    double? GridConfidenceTo,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A candidate that did not make the set. <see cref="NeededWarpPercent"/> is filled when the tempo limit
/// was the reason — it turns "the set came out short" into a specific next call.
/// </summary>
public sealed record RejectedTrackInfo(string Path, string Title, string Reason, double? NeededWarpPercent);

/// <summary>One track as placed on the set's timeline.</summary>
public sealed record SetTrackInfo(
    int Position,
    string Path,
    string Title,
    string? Artist,
    int DeckSlot,
    double StartSeconds,
    double NativeBpm,
    double WarpPercent,
    bool Warped);

/// <summary>
/// A freshly built set: what was placed, how every join was made, and what was left out and why.
/// </summary>
public sealed record DjSetResult(
    string ProjectName,
    string SavedAs,
    int TrackCount,
    double TotalSeconds,
    double TempoBpm,
    double MaxWarpPercent,
    double NativeBpmMin,
    double NativeBpmMax,
    int PhaseLockedCount,
    IReadOnlyList<SetTrackInfo> Tracks,
    IReadOnlyList<TransitionInfo> Transitions,
    int RejectedCount,
    IReadOnlyList<RejectedTrackInfo> RejectedCandidates,
    IReadOnlyDictionary<string, int> WarningSummary);

/// <summary>
/// A saved set as it can be read back. The joins are derived from the arrangement itself; the mix-point
/// provenance and the warnings from the original build are not stored, so they are absent here rather
/// than guessed.
/// </summary>
public sealed record SavedSetInfo(
    string ProjectName,
    int TrackCount,
    double TotalSeconds,
    double TempoBpm,
    IReadOnlyList<SetTrackInfo> Tracks,
    IReadOnlyList<SetJoinInfo> Joins);

/// <summary>Where two consecutive tracks overlap in a saved set.</summary>
public sealed record SetJoinInfo(
    int Index,
    string FromPath,
    string ToPath,
    double StartSeconds,
    double EndSeconds,
    double OverlapSeconds,
    double OverlapBars);

/// <summary>One rendered transition preview.</summary>
public sealed record SetPreviewClip(
    int TransitionIndex,
    string OutputPath,
    double SetStartSeconds,
    double DurationSeconds,
    string FromPath,
    string ToPath);

/// <summary>The result of rendering a set's transitions to audio.</summary>
public sealed record SetPreviewResult(
    string ProjectName,
    string OutputDirectory,
    int RenderedCount,
    double TotalRenderedSeconds,
    IReadOnlyList<SetPreviewClip> Clips);
