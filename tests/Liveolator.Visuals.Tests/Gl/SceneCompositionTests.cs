using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;

namespace Liveolator.Visuals.Tests.Gl;

public sealed class SceneCompositionTests
{
    private static VisualLayer Layer(
        string name,
        VisualSourceKind kind,
        BlendMode blend = BlendMode.Normal,
        double opacity = 1.0,
        string reference = "x")
        => new(name, new VisualSourceRef(kind, reference), Array.Empty<EffectRef>(), blend, opacity);

    private static VisualScene Scene(params VisualLayer[] layers)
        => new("scene", layers, new Dictionary<string, double>(), TransitionStyle.Cut, BeatBehavior.None);

    [Fact]
    public void Resolve_null_scene_yields_no_layers()
    {
        Assert.Empty(SceneComposition.Resolve(null));
    }

    [Fact]
    public void Resolve_preserves_layer_order_blend_and_opacity()
    {
        VisualScene scene = Scene(
            Layer("base", VisualSourceKind.Image, BlendMode.Normal, 1.0),
            Layer("glow", VisualSourceKind.Image, BlendMode.Add, 0.5));

        IReadOnlyList<ResolvedLayer> resolved = SceneComposition.Resolve(scene);

        Assert.Equal(2, resolved.Count);
        Assert.Equal("base", resolved[0].Name);
        Assert.Equal(BlendMode.Normal, resolved[0].Blend);
        Assert.Equal(1.0, resolved[0].Opacity);
        Assert.Equal("glow", resolved[1].Name);
        Assert.Equal(BlendMode.Add, resolved[1].Blend);
        Assert.Equal(0.5, resolved[1].Opacity);
    }

    [Fact]
    public void Resolve_marks_image_layers_renderable_and_video_camera_deferred()
    {
        VisualScene scene = Scene(
            Layer("img", VisualSourceKind.Image),
            Layer("clip", VisualSourceKind.VideoClip),
            Layer("cam", VisualSourceKind.Camera));

        IReadOnlyList<ResolvedLayer> resolved = SceneComposition.Resolve(scene);

        Assert.True(resolved[0].Renderable);
        Assert.False(resolved[1].Renderable);
        Assert.False(resolved[2].Renderable);
    }

    [Fact]
    public void Resolve_marks_none_layers_non_renderable()
    {
        VisualScene scene = Scene(
            Layer("img", VisualSourceKind.Image),
            Layer("off", VisualSourceKind.None, reference: ""));

        IReadOnlyList<ResolvedLayer> resolved = SceneComposition.Resolve(scene);

        Assert.True(resolved[0].Renderable);
        Assert.False(resolved[1].Renderable);
    }

    [Fact]
    public void RenderableLayers_skips_none_layers()
    {
        VisualScene scene = Scene(
            Layer("img", VisualSourceKind.Image),
            Layer("off", VisualSourceKind.None, reference: ""),
            Layer("vu", VisualSourceKind.Generator, reference: "core/vu-meter"));

        IReadOnlyList<ResolvedLayer> renderable = SceneComposition.RenderableLayers(scene);

        Assert.Equal(new[] { "img", "vu" }, renderable.Select(layer => layer.Name));
    }

    [Fact]
    public void Resolve_marks_generator_layers_renderable()
    {
        VisualScene scene = Scene(
            Layer("img", VisualSourceKind.Image),
            Layer("vu", VisualSourceKind.Generator, reference: "core/vu-meter"));

        IReadOnlyList<ResolvedLayer> resolved = SceneComposition.Resolve(scene);

        Assert.True(resolved[0].Renderable);
        Assert.True(resolved[1].Renderable);
        Assert.Equal("core/vu-meter", resolved[1].Source.Reference);
    }

    [Fact]
    public void RenderableLayers_keeps_image_and_generator_layers_skipping_video()
    {
        VisualScene scene = Scene(
            Layer("img", VisualSourceKind.Image),
            Layer("clip", VisualSourceKind.VideoClip),
            Layer("vu", VisualSourceKind.Generator, reference: "core/vu-meter"));

        IReadOnlyList<ResolvedLayer> renderable = SceneComposition.RenderableLayers(scene);

        Assert.Equal(2, renderable.Count);
        Assert.Equal("img", renderable[0].Name);
        Assert.Equal("vu", renderable[1].Name);
    }

    [Fact]
    public void Resolve_carries_the_ordered_effect_chain_to_the_renderer()
    {
        var first = new EffectRef("core/first", new Dictionary<string, double>());
        var second = new EffectRef("core/second", new Dictionary<string, double>());
        var layer = new VisualLayer(
            "fx",
            new VisualSourceRef(VisualSourceKind.Image, "x"),
            new[] { first, second },
            BlendMode.Normal,
            1.0);

        ResolvedLayer resolved = Assert.Single(SceneComposition.Resolve(Scene(layer)));

        Assert.Equal(new[] { first, second }, resolved.Effects);
    }

    [Fact]
    public void RenderableLayers_keeps_only_image_layers_in_order()
    {
        VisualScene scene = Scene(
            Layer("img-a", VisualSourceKind.Image),
            Layer("clip", VisualSourceKind.VideoClip),
            Layer("img-b", VisualSourceKind.Image));

        IReadOnlyList<ResolvedLayer> renderable = SceneComposition.RenderableLayers(scene);

        Assert.Equal(2, renderable.Count);
        Assert.Equal("img-a", renderable[0].Name);
        Assert.Equal("img-b", renderable[1].Name);
    }

    [Fact]
    public void RenderableLayers_is_empty_when_no_image_layers_exist()
    {
        VisualScene scene = Scene(Layer("clip", VisualSourceKind.VideoClip));

        Assert.Empty(SceneComposition.RenderableLayers(scene));
    }
}
