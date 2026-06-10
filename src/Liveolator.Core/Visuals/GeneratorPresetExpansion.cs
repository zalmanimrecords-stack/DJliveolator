namespace Liveolator.Core.Visuals;

/// <summary>
/// Pure expansion of a <see cref="GeneratorPreset"/> into the runtime artefacts the compositor needs
/// (doc 28). Kept GL-free and deterministic so the whole controllable-parameter wiring is unit-tested
/// off the GPU: given a preset, its generator descriptor, a target layer index, and a stable instance
/// id, it produces the generator <see cref="EffectRef"/> plus one <see cref="VisualMacro"/> per
/// controllable parameter — each addressing that generator instance so the existing
/// <c>EffectParameterResolver</c> drives the shader uniform with no new plumbing.
/// </summary>
public static class GeneratorPresetExpansion
{
    /// <summary>Derives the collision-safe macro name for a preset's controllable parameter.</summary>
    public static string MacroName(string presetId, string parameterId) => $"{presetId}.{parameterId}";

    public static GeneratorPresetBinding Expand(
        GeneratorPreset preset,
        VisualEffectDescriptor generator,
        int layerIndex,
        string instanceId)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(generator);
        if (layerIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(layerIndex), layerIndex, "Layer index must be >= 0.");
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Instance id is required.", nameof(instanceId));

        if (!string.Equals(preset.GeneratorEffectId, generator.EffectId, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Preset '{preset.PresetId}' references generator '{preset.GeneratorEffectId}' but the supplied descriptor is '{generator.EffectId}'.",
                nameof(generator));
        if (generator.Role != VisualEffectRole.Generator)
            throw new ArgumentException(
                $"Effect '{generator.EffectId}' is not a generator (role {generator.Role}); a preset can only wrap a generator.",
                nameof(generator));

        var parametersById = new Dictionary<string, VisualEffectParameter>(StringComparer.Ordinal);
        var defaults = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (VisualEffectParameter parameter in generator.Parameters)
        {
            parametersById[parameter.Id] = parameter;
            defaults[parameter.Id] = parameter.Default;
        }

        var generatorRef = new EffectRef(generator.EffectId, generator.Version, instanceId, defaults);

        var macros = new List<VisualMacro>(preset.Controllable.Count);
        var initialValues = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (ControllableParameter controllable in preset.Controllable)
        {
            if (!parametersById.TryGetValue(controllable.Id, out VisualEffectParameter? parameter))
                throw new ArgumentException(
                    $"Controllable parameter '{controllable.Id}' is not declared by generator '{generator.EffectId}'.",
                    nameof(preset));

            string macroName = MacroName(preset.PresetId, controllable.Id);
            macros.Add(new VisualMacro(
                macroName,
                parameter.Min,
                parameter.Max,
                parameter.Default,
                new MacroTarget(layerIndex, instanceId, controllable.Id)));
            initialValues[macroName] = Normalize(parameter.Default, parameter.Min, parameter.Max);
        }

        return new GeneratorPresetBinding(generatorRef, macros, initialValues);
    }

    /// <summary>Inverse of <see cref="VisualMacro.Resolve"/>: maps a value in [min,max] back to 0..1.</summary>
    private static double Normalize(double value, double min, double max)
        => max <= min ? 0.0 : Math.Clamp((value - min) / (max - min), 0.0, 1.0);
}
