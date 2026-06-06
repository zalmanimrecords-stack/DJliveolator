using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists the music library's state across runs: the analyzed track catalog (doc 13/16 cache)
/// and the scan-folder roots the user added. Implemented in <c>Liveolator.Media</c>; the seam lives
/// in Core so the UI/engines depend only on the abstraction (Core iron rule #3), keeping them
/// unit-testable with a fake and free of any filesystem dependency.
/// </summary>
/// <remarks>
/// Loads are tolerant: a missing, unreadable, or incompatible-version file yields an empty result
/// (triggering a clean re-scan) and a warning, never an exception (global standards #16/#26). Saves
/// are atomic (temp-then-move) so an interrupted write never corrupts the persisted state.
/// </remarks>
public interface IMusicCatalogStore
{
    /// <summary>
    /// Loads the persisted track catalog, or an empty list when none exists, it is unreadable, or it
    /// was written by an incompatible schema version.
    /// </summary>
    Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the analyzed track catalog so a later run re-loads it instead of re-analyzing.</summary>
    Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the persisted scan-folder roots, or an empty list when none exist, the file is
    /// unreadable, or it was written by an incompatible schema version.
    /// </summary>
    Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the scan-folder roots so the user does not re-add them on the next run.</summary>
    Task SaveScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the folders the user designated as "samples" (the classifier override), or an empty list
    /// when none exist / the file is unreadable / it was written by an incompatible version.
    /// </summary>
    Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the sample-folder designations so the kind split survives a restart.</summary>
    Task SaveSampleFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default);
}
