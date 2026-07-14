namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists named <see cref="Playlist.Playlist"/>s (curated sets) across runs. Implemented in
/// <c>Liveolator.Media</c> (one file per playlist under the per-user <c>live/playlists/</c> layout,
/// doc 13); the seam lives in Core so the UI depends only on the abstraction (Core iron rule #3) and
/// is unit-testable with a fake.
/// </summary>
/// <remarks>
/// Loads are tolerant: a missing, unreadable, or incompatible-version playlist yields <c>null</c> and
/// a warning, never an exception (global standards #16/#26). Saves are atomic (temp-then-move).
/// </remarks>
public interface IPlaylistStore
{
    /// <summary>Lists the saved playlist names (display names), in a stable order.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a saved playlist by name, or <c>null</c> when it is missing/unreadable/incompatible.</summary>
    Task<Playlist.Playlist?> LoadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Saves (creates or replaces) a playlist under its name.</summary>
    Task SaveAsync(Playlist.Playlist playlist, CancellationToken cancellationToken = default);

    /// <summary>Deletes a saved playlist by name; a missing playlist is a no-op.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
