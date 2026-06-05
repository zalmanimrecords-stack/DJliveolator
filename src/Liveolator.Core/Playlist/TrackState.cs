namespace Liveolator.Core.Playlist;

/// <summary>Where a track sits in the live queue (doc 09).</summary>
public enum TrackState
{
    /// <summary>Currently playing.</summary>
    Now,

    /// <summary>The immediate next track.</summary>
    Next,

    /// <summary>Queued after Next.</summary>
    Later,

    /// <summary>Already played (history).</summary>
    Played,
}
