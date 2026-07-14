namespace Liveolator.Core.Library;

/// <summary>
/// Per-folder roll-up of a scanned catalog: how many tracks live under a folder root and how
/// their offline analysis turned out. Drives the Libraries folder-status view (one row per
/// added scan folder). Pure data — derived from the catalog, never from a live scan.
/// </summary>
public sealed record FolderCatalogSummary(
    string Folder,
    int TrackCount,
    int Ok,
    int PartiallyAnalyzed,
    int Failed);
