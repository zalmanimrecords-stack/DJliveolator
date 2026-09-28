using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists the user-curated genre parent/child hierarchy (<see cref="GenreHierarchy"/>) across runs.
/// Kept as a <em>separate</em> store from the music catalog and from <see cref="IHotCueStore"/>, for the
/// same reason cues are separate: this data changes on its own cadence, and a dedicated file is
/// backward-compatible by construction — an existing catalog or cue file is never touched, and a genre
/// with no assigned parent behaves exactly as the flat genre tag always did (global standards #20/#22).
/// </summary>
/// <remarks>
/// The seam lives in Core so engines/UI depend only on the abstraction (Core iron rule #3); the JSON
/// implementation lives in <c>Liveolator.Media</c>. Loads are tolerant: a missing, unreadable, or
/// incompatible-version file yields an empty (all-top-level) tree and a warning, never an exception
/// (global standards #16/#26).
/// </remarks>
public interface IGenreHierarchyStore
{
    /// <summary>
    /// Loads the saved genre hierarchy, or an empty tree when the file is missing, unreadable, or was
    /// written by an incompatible schema version.
    /// </summary>
    Task<GenreHierarchy> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the full genre hierarchy, replacing whatever was previously stored.</summary>
    Task SaveAsync(GenreHierarchy hierarchy, CancellationToken cancellationToken = default);
}
