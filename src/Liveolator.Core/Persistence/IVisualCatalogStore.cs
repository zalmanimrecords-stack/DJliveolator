using Liveolator.Core.Library.Visual;

namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists the visual-media library's state across runs: the catalogued image/video assets (doc 13
/// cache) and the scan-folder roots the user added. Mirrors <see cref="IMusicCatalogStore"/> for the
/// visual domain. Implemented in <c>Liveolator.Media</c>; the seam lives in Core so the UI/engines
/// depend only on the abstraction (Core iron rule #3), keeping them unit-testable with a fake and free
/// of any filesystem dependency.
/// </summary>
/// <remarks>
/// Loads are tolerant: a missing, unreadable, or incompatible-version file yields an empty result
/// (triggering a clean re-scan) and a warning, never an exception (global standards #16/#26). Saves
/// are atomic (temp-then-move) so an interrupted write never corrupts the persisted state.
/// </remarks>
public interface IVisualCatalogStore
{
    /// <summary>
    /// Loads the persisted visual-asset catalog, or an empty list when none exists, it is unreadable,
    /// or it was written by an incompatible schema version.
    /// </summary>
    Task<IReadOnlyList<VisualAsset>> LoadVisualAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the catalogued visual assets so a later run re-loads them instead of re-probing.</summary>
    Task SaveVisualAsync(IEnumerable<VisualAsset> assets, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the persisted visual scan-folder roots, or an empty list when none exist, the file is
    /// unreadable, or it was written by an incompatible schema version.
    /// </summary>
    Task<IReadOnlyList<string>> LoadVisualScanFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the visual scan-folder roots so the user does not re-add them on the next run.</summary>
    Task SaveVisualScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default);
}
