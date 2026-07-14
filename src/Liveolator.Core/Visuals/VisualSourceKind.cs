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

    /// <summary>
    /// A generator: the layer's pixels are produced by a GLSL generator shader rather than decoded from
    /// an asset (doc 26 — the basis for generative add-ons such as a VU meter). The owning
    /// <see cref="VisualSourceRef.Reference"/> holds the generator effect id, resolved against the
    /// <see cref="IVisualEffectRegistry"/> to a <see cref="VisualEffectRole.Generator"/> descriptor.
    /// </summary>
    Generator,

    /// <summary>
    /// An empty layer: the slot is occupied but draws nothing, so it is composited as if absent. Lets a
    /// performer switch a layer "off" from the UI without removing it from the scene. Carries no
    /// <see cref="VisualSourceRef.Reference"/> and resolves as non-renderable, so the compositor skips it
    /// (doc 08 — the same graceful-skip path as deferred video/camera sources).
    /// </summary>
    None,
}
