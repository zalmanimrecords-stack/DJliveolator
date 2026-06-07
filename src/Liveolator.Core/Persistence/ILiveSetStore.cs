namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists the single, live DJ set — the current Now/Next/Later queue, as an ordered list of track
/// paths — so it survives a restart and the DJ tab opens where the last run left off (doc 13).
/// Distinct from <see cref="IPlaylistStore"/>: that holds the user's *named* saved playlists; this is
/// the one unnamed working set, written automatically on every edit. Implemented in
/// <c>Liveolator.Media</c>; the seam lives in Core so the UI/composition depend only on the abstraction
/// (Core iron rule #3) and it is unit-testable with a fake.
/// </summary>
/// <remarks>
/// Loads are tolerant: a missing, unreadable, or incompatible-version file yields <c>null</c> and a
/// warning, never an exception (global standards #16/#26). Saves are atomic (temp-then-move).
/// </remarks>
public interface ILiveSetStore
{
    /// <summary>
    /// Loads the saved set as an ordered list of track paths (Now first), or <c>null</c> when nothing
    /// is saved or the file is unreadable/incompatible.
    /// </summary>
    Task<IReadOnlyList<string>?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves (creates or replaces) the current set; an empty list clears the saved set.</summary>
    Task SaveAsync(IReadOnlyList<string> trackPaths, CancellationToken cancellationToken = default);
}
