namespace Liveolator.Core.Visuals;

/// <summary>
/// A MilkDrop-style preset (doc 28): a full-frame GLSL <see cref="VisualEffectRole.Generator"/>
/// (referenced by <see cref="GeneratorEffectId"/>) together with the declaration of which of its
/// parameters are exposed as live, externally controllable knobs. The set is bounded to keep the
/// performer's control surface focused — see <see cref="MaxControllableParameters"/>.
/// </summary>
public sealed record GeneratorPreset
{
    /// <summary>The most controllable parameters a single preset may expose (doc 28 requirement).</summary>
    public const int MaxControllableParameters = 5;

    public GeneratorPreset(
        string presetId,
        string name,
        string generatorEffectId,
        string generatorVersion,
        IReadOnlyList<ControllableParameter> controllable)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            throw new ArgumentException("Preset id is required.", nameof(presetId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Preset name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(generatorEffectId))
            throw new ArgumentException("Generator effect id is required.", nameof(generatorEffectId));
        Controllable = controllable ?? throw new ArgumentNullException(nameof(controllable));

        if (controllable.Count > MaxControllableParameters)
            throw new ArgumentException(
                $"A preset may expose at most {MaxControllableParameters} controllable parameters.",
                nameof(controllable));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ControllableParameter parameter in controllable)
        {
            if (!seen.Add(parameter.Id))
                throw new ArgumentException(
                    $"Duplicate controllable parameter id '{parameter.Id}'.", nameof(controllable));
        }

        PresetId = presetId;
        Name = name;
        GeneratorEffectId = generatorEffectId;
        GeneratorVersion = string.IsNullOrWhiteSpace(generatorVersion) ? "1.0.0" : generatorVersion;
    }

    public string PresetId { get; init; }
    public string Name { get; init; }
    public string GeneratorEffectId { get; init; }
    public string GeneratorVersion { get; init; }
    public IReadOnlyList<ControllableParameter> Controllable { get; init; }
}
