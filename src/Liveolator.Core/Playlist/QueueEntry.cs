namespace Liveolator.Core.Playlist;

/// <summary>
/// One entry in the live queue. <see cref="Id"/> is a stable handle the UI uses to reorder/remove
/// without depending on position (doc 09).
/// </summary>
/// <param name="TrackPath">Path of the track file.</param>
/// <param name="Id">Stable identity for this queue slot.</param>
/// <param name="State">The entry's position in the queue.</param>
public sealed record QueueEntry(string TrackPath, Guid Id, TrackState State);
