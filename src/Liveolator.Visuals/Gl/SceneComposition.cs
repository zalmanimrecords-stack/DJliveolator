using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Pure resolution of a <see cref="VisualScene"/>'s layer stack into the ordered, per-frame draw list
/// the compositor renders (doc 08 — "a scene is a real layer stack, not a single preset"). GL-free,
/// so the bottom→top ordering, per-layer opacity/blend carry-over, and the image-only renderability
/// gate all unit-test off the GPU. <see cref="GlVisualPerformanceEngine"/> calls this each time the
/// active scene changes to (re)build its layer textures.
/// </summary>
public static class SceneComposition
{
    /// <summary>
    /// Resolves <paramref name="scene"/> into its layers in composite order (bottom→top, matching
    /// <see cref="VisualScene.Layers"/>). Each layer carries its blend mode, opacity, and whether the
    /// compositor can render it in this slice (image sources only; video/camera resolve as
    /// non-renderable so callers degrade gracefully — doc 08 error handling). A null/empty scene
    /// yields an empty list.
    /// </summary>
    public static IReadOnlyList<ResolvedLayer> Resolve(VisualScene? scene)
    {
        if (scene is null || scene.Layers.Count == 0)
            return Array.Empty<ResolvedLayer>();

        var resolved = new List<ResolvedLayer>(scene.Layers.Count);
        foreach (VisualLayer layer in scene.Layers)
        {
            resolved.Add(new ResolvedLayer(
                layer.Name,
                layer.Source,
                layer.Blend,
                layer.Opacity,
                Renderable: layer.Source.Kind == VisualSourceKind.Image));
        }
        return resolved;
    }

    /// <summary>The renderable layers of <paramref name="scene"/> in composite order (image sources only).</summary>
    public static IReadOnlyList<ResolvedLayer> RenderableLayers(VisualScene? scene)
    {
        IReadOnlyList<ResolvedLayer> all = Resolve(scene);
        if (all.Count == 0)
            return all;

        var renderable = new List<ResolvedLayer>(all.Count);
        foreach (ResolvedLayer layer in all)
        {
            if (layer.Renderable)
                renderable.Add(layer);
        }
        return renderable;
    }
}
