namespace Liveolator.Core.Studio;

/// <summary>
/// One track clip placed on a deck lane in the STUDIO arrangement: which deck plays it
/// (<see cref="DeckSlot"/> 0-3), the source file, where it starts on the timeline
/// (<see cref="TimelineStartSeconds"/>), and the trim into the source (<see cref="SourceIn"/> to
/// <see cref="SourceOut"/>; null out = play to the end of the file). Pure data — paths only.
/// </summary>
public sealed record StudioClip(
    int DeckSlot,
    string TrackPath,
    double TimelineStartSeconds,
    TimeSpan SourceIn,
    TimeSpan? SourceOut)
{
    /// <summary>The trimmed source length when both ends are known; null when the out point is open.</summary>
    public TimeSpan? SourceDuration => SourceOut is { } outPoint ? outPoint - SourceIn : null;

    /// <summary>Timeline end position when the source length is known; null for an open-ended clip.</summary>
    public double? TimelineEndSeconds
        => SourceDuration is { } d ? TimelineStartSeconds + d.TotalSeconds : null;
}
