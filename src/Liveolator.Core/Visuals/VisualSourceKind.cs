namespace Liveolator.Core.Visuals;

/// <summary>The kind of GPU texture source feeding a layer (doc 08 compositor recap).</summary>
public enum VisualSourceKind
{
    /// <summary>A still image decoded to a texture.</summary>
    Image,

    /// <summary>A video clip decoded frame-by-frame (play/loop/scrub/speed).</summary>
    VideoClip,

    /// <summary>A live camera / capture device.</summary>
    Camera,
}
