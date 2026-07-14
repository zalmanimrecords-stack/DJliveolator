namespace Liveolator.Core.Library.Doctor;

public enum LibraryIssueKind
{
    MissingFile,
    OfflineFolder,
    DuplicateCandidate,
    BrokenAnalysis,
    UnanalyzedTrack,
    LowConfidenceAnalysis,
    UnreachableVisualAsset,
}

public enum LibraryRepairConfidence
{
    Low,
    Medium,
    High,
}

public enum LibraryRepairActionKind
{
    RelocateCatalogPath,
    RemoveFromCatalog,
    MergeDuplicateCatalogEntries,
    ReanalyzeTrack,
}

public sealed record LibraryIssue(
    string Id,
    LibraryIssueKind Kind,
    MediaIdentityKind MediaKind,
    string Path,
    string Title,
    string Message,
    LibraryRepairConfidence Confidence,
    IReadOnlyList<string> RelatedPaths);

public sealed record LibraryRepairAction(
    LibraryRepairActionKind Kind,
    string SourcePath,
    string? TargetPath,
    LibraryRepairConfidence Confidence,
    string Description);

public sealed record LibraryRepairPreview(
    int TracksRelocated,
    int TracksRemovedFromCatalog,
    int DuplicateGroupsMerged,
    int PlaylistsAffected,
    int LiveSetsAffected,
    int VisualLinksAffected,
    IReadOnlyList<string> Blockers);

public sealed record LibraryRepairPlan(
    IReadOnlyList<LibraryRepairAction> Actions,
    LibraryRepairPreview Preview)
{
    public bool CanApply => Preview.Blockers.Count == 0;
}

public sealed record LibraryDoctorReport(
    IReadOnlyList<LibraryIssue> Issues,
    IReadOnlyList<DuplicateGroup<IMediaEntry>> DuplicateGroups,
    IReadOnlyList<string> OfflineFolders)
{
    public int MissingCount => Issues.Count(i =>
        i.Kind is LibraryIssueKind.MissingFile or LibraryIssueKind.UnreachableVisualAsset);

    public int BrokenCount => Issues.Count(i =>
        i.Kind is LibraryIssueKind.BrokenAnalysis or LibraryIssueKind.UnanalyzedTrack or LibraryIssueKind.LowConfidenceAnalysis);

    public int DuplicateCount => DuplicateGroups.Count;
}

