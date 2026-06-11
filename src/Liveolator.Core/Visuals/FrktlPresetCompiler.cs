namespace Liveolator.Core.Visuals;

/// <summary>
/// Pure mapping of a validated <see cref="FrktlPresetFile"/> (doc 29) into the runtime pair the
/// compositor needs: a <see cref="VisualEffectDescriptor"/> (Generator role, its shader on disk) and a
/// <see cref="GeneratorPreset"/> exposing every declared parameter as a controllable knob. The preset id
/// equals the effect id so the derived macros (<c>&lt;effectId&gt;.&lt;paramId&gt;</c>) line up with the
/// instance id the renderer assigns a generator layer — no scene-model plumbing needed.
/// </summary>
public static class FrktlPresetCompiler
{
    public sealed record Compiled(VisualEffectDescriptor Descriptor, GeneratorPreset Preset);

    public static Compiled Compile(FrktlPresetFile file, string effectId, string packageId, string shaderPath)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderPath);

        IReadOnlyList<FrktlPresetParameter> parameters = file.Parameters ?? Array.Empty<FrktlPresetParameter>();

        var descriptor = new VisualEffectDescriptor(
            effectId,
            "1.0.0",
            packageId,
            shaderPath,
            parameters.Select(p => new VisualEffectParameter(p.Id, p.Uniform, p.Min, p.Max, p.Default)).ToArray(),
            Role: VisualEffectRole.Generator);

        var preset = new GeneratorPreset(
            effectId, // preset id == effect id: macros target the generator by its effect id
            file.Name,
            effectId,
            "1.0.0",
            parameters.Select(p => new ControllableParameter(p.Id, p.Label)).ToArray());

        return new Compiled(descriptor, preset);
    }
}
