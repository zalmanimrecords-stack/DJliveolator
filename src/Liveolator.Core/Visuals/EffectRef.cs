namespace Liveolator.Core.Visuals;

/// <summary>
/// A reference to one GLSL effect in a layer's chain, with its default parameter values. The
/// concrete shader lives in the compositor; this record is the serializable description (doc 08).
/// </summary>
/// <param name="EffectId">Identifies the shader (e.g. "echo", "kaleidoscope").</param>
/// <param name="Defaults">Default parameter values by name (e.g. "feedback" → 0.5).</param>
public sealed record EffectRef(string EffectId, IReadOnlyDictionary<string, double> Defaults);
