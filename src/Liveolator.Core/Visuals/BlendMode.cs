namespace Liveolator.Core.Visuals;

/// <summary>
/// How a layer composites over the layers beneath it. Maps to the compositor's GLSL blend
/// (doc 08); multiple layers render simultaneously (no single-preset limit).
/// </summary>
public enum BlendMode
{
    Normal,
    Add,
    Multiply,
    Screen,
    Overlay,
}
