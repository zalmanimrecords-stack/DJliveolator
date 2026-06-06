namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists per-track hot/primary cue points across runs (doc 11/13) so a DJ's stored cues survive
/// app restarts instead of living only in deck RAM. Kept as a <em>separate</em> store from the music
/// catalog (<see cref="IMusicCatalogStore"/>) on purpose: cues change far more often than the analyzed
/// catalog and must never invalidate it, and a separate file is fully backward-compatible with
/// existing catalog files (no schema bump, no migration risk — global standards #20/#22).
/// </summary>
/// <remarks>
/// The seam lives in Core so engines/UI depend only on the abstraction (Core iron rule #3); the JSON
/// implementation lives in <c>Liveolator.Media</c>. Loads are tolerant: a missing, unreadable, or
/// incompatible-version file yields an empty result and a warning, never an exception (global #16/#26).
/// </remarks>
public interface IHotCueStore
{
    /// <summary>
    /// Loads the stored cue record for a track by its file path, or null when the track has no saved
    /// cues, the store is unreadable, or it was written by an incompatible schema version.
    /// </summary>
    Task<TrackCueRecord?> LoadAsync(string trackPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves (inserts or replaces) the cue record for its <see cref="TrackCueRecord.TrackPath"/>,
    /// leaving every other track's cues untouched.
    /// </summary>
    Task SaveAsync(TrackCueRecord record, CancellationToken cancellationToken = default);

    /// <summary>Removes any stored cues for a track path. A no-op when the track has none.</summary>
    Task DeleteAsync(string trackPath, CancellationToken cancellationToken = default);
}
