namespace Liveolator.Core.Audio;

/// <summary>
/// Opens an upcoming track ahead of time so the handoff to it is near-instant (doc 09 "Preload").
/// The realtime backend pre-buffers the file/stream; the seam lives in Core so the pure
/// <c>NextTrackPreloader</c> sequencing can be unit-tested against a fake with no native audio.
/// </summary>
/// <remarks>
/// Implementations must be tolerant: a failed preload is logged and dropped, never thrown — a bad
/// upcoming track must not crash the show or stall the queue (global standards #16/#26). A new
/// <see cref="Preload"/> for a different track supersedes any in-flight one (live reorder safety).
/// </remarks>
public interface IDeckPreloader
{
    /// <summary>
    /// Prepare <paramref name="trackPath"/> for an imminent load. A null/empty path clears any
    /// pending preload (the queue ran dry). Safe to call repeatedly; the latest path wins.
    /// </summary>
    void Preload(string? trackPath);
}
