namespace Liveolator.Core.Visuals.TrackPrograms;

/// <summary>How a visual source behaves when its media duration is shorter than its track cue.</summary>
public enum VisualPlaybackMode
{
    /// <summary>Play once, then hold the last frame.</summary>
    Once,

    /// <summary>Repeat the selected source range until the cue ends.</summary>
    Loop,
}
