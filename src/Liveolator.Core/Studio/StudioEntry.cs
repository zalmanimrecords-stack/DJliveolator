namespace Liveolator.Core.Studio;

/// <summary>
/// One track in a planned STUDIO set: the track path, optional in/out trim points, and the
/// transition that leads <em>into</em> it from the previous entry. <see cref="TransitionIn"/> is
/// null for the first entry (nothing precedes it). Pure data — paths only; BPM/key/cues are looked
/// up from the library at display and render time (mirrors <see cref="Playlist.Playlist"/>).
/// </summary>
public sealed record StudioEntry(
    string TrackPath,
    TimeSpan? InPoint = null,
    TimeSpan? OutPoint = null,
    StudioTransition? TransitionIn = null);
