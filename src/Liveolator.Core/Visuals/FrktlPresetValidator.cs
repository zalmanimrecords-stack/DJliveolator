namespace Liveolator.Core.Visuals;

/// <summary>The outcome of validating a <see cref="FrktlPresetFile"/>: valid, or invalid with a reason.</summary>
public sealed record FrktlPresetValidation(bool IsValid, string? Error)
{
    public static FrktlPresetValidation Ok { get; } = new(true, null);
    public static FrktlPresetValidation Fail(string error) => new(false, error);
}

/// <summary>
/// Pure structural validation of a <see cref="FrktlPresetFile"/> (doc 29) before it is compiled into a
/// generator + preset. Cheap, GL-free checks only — the real GLSL compile happens on the GPU; this guards
/// the contract that makes a file loadable: a name, ≤5 well-formed parameters (unique ids/uniforms, sane
/// ranges), and a shader that is ASCII-only and at least references its declared uniforms and the output.
/// </summary>
public static class FrktlPresetValidator
{
    public static FrktlPresetValidation Validate(FrktlPresetFile? file)
    {
        if (file is null)
            return FrktlPresetValidation.Fail("Preset is null.");
        if (string.IsNullOrWhiteSpace(file.Name))
            return FrktlPresetValidation.Fail("Preset name is required.");

        IReadOnlyList<FrktlPresetParameter> parameters = file.Parameters ?? Array.Empty<FrktlPresetParameter>();
        if (parameters.Count > GeneratorPreset.MaxControllableParameters)
            return FrktlPresetValidation.Fail(
                $"A preset may declare at most {GeneratorPreset.MaxControllableParameters} parameters (found {parameters.Count}).");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var uniforms = new HashSet<string>(StringComparer.Ordinal);
        foreach (FrktlPresetParameter parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Id))
                return FrktlPresetValidation.Fail("A parameter is missing its id.");
            if (string.IsNullOrWhiteSpace(parameter.Uniform))
                return FrktlPresetValidation.Fail($"Parameter '{parameter.Id}' is missing its uniform.");
            if (string.IsNullOrWhiteSpace(parameter.Label))
                return FrktlPresetValidation.Fail($"Parameter '{parameter.Id}' is missing its label.");
            if (parameter.Max < parameter.Min)
                return FrktlPresetValidation.Fail($"Parameter '{parameter.Id}' has max < min.");
            if (parameter.Default < parameter.Min || parameter.Default > parameter.Max)
                return FrktlPresetValidation.Fail($"Parameter '{parameter.Id}' default is outside [min, max].");
            if (!ids.Add(parameter.Id))
                return FrktlPresetValidation.Fail($"Duplicate parameter id '{parameter.Id}'.");
            if (!uniforms.Add(parameter.Uniform))
                return FrktlPresetValidation.Fail($"Duplicate parameter uniform '{parameter.Uniform}'.");
        }

        string shader = file.Shader ?? string.Empty;
        if (string.IsNullOrWhiteSpace(shader))
            return FrktlPresetValidation.Fail("Shader source is required.");
        // Non-ASCII bytes trip some GL preprocessors ("premature EOF" on Intel) — reject up front.
        if (shader.Any(ch => ch > '\x7F'))
            return FrktlPresetValidation.Fail("Shader contains non-ASCII characters (GLSL must be ASCII-only).");
        if (!shader.Contains("void main", StringComparison.Ordinal))
            return FrktlPresetValidation.Fail("Shader has no main() entry point.");
        if (!shader.Contains("fragColor", StringComparison.Ordinal))
            return FrktlPresetValidation.Fail("Shader never writes fragColor.");
        foreach (FrktlPresetParameter parameter in parameters)
        {
            if (!shader.Contains(parameter.Uniform, StringComparison.Ordinal))
                return FrktlPresetValidation.Fail(
                    $"Shader does not declare the uniform '{parameter.Uniform}' for parameter '{parameter.Id}'.");
        }

        return FrktlPresetValidation.Ok;
    }
}
