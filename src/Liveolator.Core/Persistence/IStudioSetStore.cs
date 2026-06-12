namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists named <see cref="Studio.StudioSet"/>s (pre-show planned sets) across runs. Implemented
/// in <c>Liveolator.Media</c> (one file per set under <c>live/studio-sets/</c>, separate from
/// <c>live/playlists/</c>); the seam lives in Core so the UI depends only on the abstraction (Core
/// iron rule #3) and is unit-testable with a fake. Mirrors <see cref="IPlaylistStore"/>.
/// </summary>
/// <remarks>
/// Loads are tolerant: a missing, unreadable, or incompatible-version set yields <c>null</c> and a
/// warning, never an exception (global standards #16/#26). Saves are atomic (temp-then-move).
/// </remarks>
public interface IStudioSetStore
{
    /// <summary>Lists the saved set names (display names), in a stable order.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a saved set by name, or <c>null</c> when it is missing/unreadable/incompatible.</summary>
    Task<Studio.StudioSet?> LoadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Saves (creates or replaces) a set under its name.</summary>
    Task SaveAsync(Studio.StudioSet set, CancellationToken cancellationToken = default);

    /// <summary>Deletes a saved set by name; a missing set is a no-op.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
