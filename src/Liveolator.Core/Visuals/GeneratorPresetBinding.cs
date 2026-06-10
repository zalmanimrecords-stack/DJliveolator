namespace Liveolator.Core.Visuals;

/// <summary>
/// The result of expanding a <see cref="GeneratorPreset"/> against its generator descriptor for a
/// concrete layer (doc 28): the generator <see cref="EffectRef"/> to install on that layer, the
/// derived <see cref="VisualMacro"/>s (one per controllable parameter, already targeting the
/// generator instance), and the normalized 0..1 starting value for each macro so UI knobs and the
/// engine seed to the descriptor defaults.
/// </summary>
public sealed record GeneratorPresetBinding(
    EffectRef Generator,
    IReadOnlyList<VisualMacro> Macros,
    IReadOnlyDictionary<string, double> InitialMacroValues);
