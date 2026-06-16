namespace Liveolator.Core.Studio;

/// <summary>
/// One track clip placed on a deck lane in the STUDIO arrangement: which deck plays it
/// (<see cref="DeckSlot"/> 0-3), the source file, where it starts on the timeline
/// (<see cref="TimelineStartSeconds"/>), and the trim into the source (<see cref="SourceIn"/> to
/// <see cref="SourceOut"/>; null out = play to the end of the file). Pure data — paths only.
/// <para><see cref="SourceBpm"/> is the clip track's analyzed tempo (0 = unknown); with
/// <see cref="WarpEnabled"/> the clip is time-stretched (pitch preserved) to the project tempo.
/// The trailing optional params keep older saved projects loadable (they default).</para>
/// </summary>
public sealed record StudioClip(
    int DeckSlot,
    string TrackPath,
    double TimelineStartSeconds,
    TimeSpan SourceIn,
    TimeSpan? SourceOut,
    double SourceBpm = 0.0,
    bool WarpEnabled = false)
{
    /// <summary>True when this clip can be warped: warp is on and the source tempo is known.</summary>
    public bool CanWarp => WarpEnabled && SourceBpm > 0.0;

    /// <summary>The trimmed source length when both ends are known; null when the out point is open.</summary>
    public TimeSpan? SourceDuration => SourceOut is { } outPoint ? outPoint - SourceIn : null;

    /// <summary>Timeline end position when the source length is known; null for an open-ended clip.</summary>
    public double? TimelineEndSeconds
        => SourceDuration is { } d ? TimelineStartSeconds + d.TotalSeconds : null;
}
