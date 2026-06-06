using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// One layer of a <see cref="VisualScene"/> resolved for a frame: its source, the blend mode used to
/// composite it over the layers beneath, its opacity, and whether the compositor can render it in
/// this slice. Pure data (no GL) so the scene→layer mapping unit-tests off the GPU; the renderer
/// turns each <see cref="Renderable"/> layer into a textured, blended quad draw bottom→top.
/// </summary>
/// <param name="Name">The source layer's name (for diagnostics).</param>
/// <param name="Source">The texture source reference.</param>
/// <param name="Blend">How this layer composites over those beneath it.</param>
/// <param name="Opacity">Effective opacity in 0..1 (the layer's own opacity; macro-driven opacity is later).</param>
/// <param name="Renderable">
/// True when this slice can draw the layer (an <see cref="VisualSourceKind.Image"/> source). Video and
/// camera sources are deferred, so they resolve as non-renderable and the renderer skips them rather
/// than crashing the show.
/// </param>
public readonly record struct ResolvedLayer(
    string Name,
    VisualSourceRef Source,
    BlendMode Blend,
    double Opacity,
    bool Renderable);
