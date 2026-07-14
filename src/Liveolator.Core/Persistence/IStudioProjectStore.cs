namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists named <see cref="Studio.StudioProject"/>s (DAW-timeline arrangements) across runs.
/// Implemented in <c>Liveolator.Media</c> (one JSON file per project under
/// <c>live/studio-projects/</c>); the seam lives in Core so the UI depends only on the abstraction
/// (Core iron rule #3) and is unit-testable with a fake. Mirrors <see cref="IPlaylistStore"/>.
/// </summary>
/// <remarks>
/// Loads are tolerant: a missing, unreadable, or incompatible-version project yields <c>null</c>
/// and a warning, never an exception (global standards #16/#26). Saves are atomic (temp-then-move).
/// </remarks>
public interface IStudioProjectStore
{
    /// <summary>Lists the saved project names (display names), in a stable order.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads a saved project by name, or <c>null</c> when missing/unreadable/incompatible.</summary>
    Task<Studio.StudioProject?> LoadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Saves (creates or replaces) a project under its name.</summary>
    Task SaveAsync(Studio.StudioProject project, CancellationToken cancellationToken = default);

    /// <summary>Deletes a saved project by name; a missing project is a no-op.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
