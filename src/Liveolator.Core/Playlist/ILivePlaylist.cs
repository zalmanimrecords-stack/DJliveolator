using Liveolator.Core.Beat;

namespace Liveolator.Core.Playlist;

/// <summary>
/// A performance-editable Now/Next/Later queue. Editing <see cref="Upcoming"/> never touches
/// <see cref="Now"/>, so playback continues uninterrupted — the central success criterion (doc 09).
/// This is the live-editing model; the audio binding subscribes to <see cref="NowChanged"/> and
/// drives the underlying player.
/// </summary>
public interface ILivePlaylist
{
    /// <summary>The playing track, or null when the queue is exhausted.</summary>
    QueueEntry? Now { get; }

    /// <summary>The editable future: Next first, then Later, in play order.</summary>
    IReadOnlyList<QueueEntry> Upcoming { get; }

    /// <summary>True when the queue advances automatically at end of track.</summary>
    bool AutoAdvance { get; }

    /// <summary>Replaces the whole queue; the first track becomes Now.</summary>
    void Load(IEnumerable<string> trackPaths);

    /// <summary>Appends a track to the end of Later.</summary>
    void Append(string trackPath);

    /// <summary>Inserts a track immediately after Now (it becomes Next).</summary>
    void InsertNext(string trackPath);

    /// <summary>Reorders an upcoming entry to <paramref name="toIndex"/>; stale ids are ignored.</summary>
    void Move(Guid id, int toIndex);

    /// <summary>Removes a future entry; Now is protected and stale ids are ignored.</summary>
    void RemoveFuture(Guid id);

    /// <summary>Enables/disables auto-advance at end of track.</summary>
    void SetAutoAdvance(bool on);

    /// <summary>Advances to the next track immediately.</summary>
    void SkipNow();

    /// <summary>Schedules a safe skip on the next beat/bar boundary via the beat clock (doc 03).</summary>
    void SkipOn(Quantize when, int everyN = 1);

    /// <summary>Called by the audio binding when the current track finishes; advances if auto-advance is on.</summary>
    void NotifyTrackEnded();

    /// <summary>Raised when Now changes (null when the queue is exhausted).</summary>
    event EventHandler<QueueEntry?>? NowChanged;

    /// <summary>
    /// Raised after any mutation that changes the set — Now or the upcoming order/contents
    /// (load, append, insert, move, remove, advance). Lets a persistence binding snapshot the
    /// current set so it survives a restart (doc 13), without the queue knowing about storage.
    /// </summary>
    event EventHandler? Changed;
}
