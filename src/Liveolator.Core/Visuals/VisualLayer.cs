namespace Liveolator.Core.Visuals;

/// <summary>
/// One composited layer: a source, a GLSL effect chain, a blend mode, and an opacity. Layers stack
/// and blend bottom→top; there is no "one active preset" limit (doc 08).
/// </summary>
public sealed record VisualLayer
{
    /// <param name="opacity">Layer opacity in 0..1.</param>
    public VisualLayer(
        string name,
        VisualSourceRef source,
        IReadOnlyList<EffectRef> effects,
        BlendMode blend,
        double opacity)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        if (opacity is < 0 or > 1 || double.IsNaN(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be in 0..1.");

        Blend = blend;
        Opacity = opacity;
    }

    public string Name { get; init; }

    public VisualSourceRef Source { get; init; }

    public IReadOnlyList<EffectRef> Effects { get; init; }

    public BlendMode Blend { get; init; }

    public double Opacity { get; init; }
}
