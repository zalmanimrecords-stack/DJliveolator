namespace Liveolator.Core.Visuals;

public sealed record VisualEffectParameter(
    string Id,
    string Uniform,
    double Min,
    double Max,
    double Default);

/// <summary>Validated metadata for a GLSL fragment effect supplied by an extension package.</summary>
public sealed record VisualEffectDescriptor(
    string EffectId,
    string Version,
    string PackageId,
    string ShaderPath,
    IReadOnlyList<VisualEffectParameter> Parameters,
    int MinimumOpenGlMajor = 3,
    int MinimumOpenGlMinor = 3);

public interface IVisualEffectRegistry
{
    IReadOnlyList<VisualEffectDescriptor> Effects { get; }
    bool TryGet(string effectId, string? version, out VisualEffectDescriptor descriptor);
    void ReplacePackage(string packageId, IEnumerable<VisualEffectDescriptor> effects);
    void RemovePackage(string packageId);
}

public sealed record VisualShaderProbeResult(
    bool IsValid,
    string? Error,
    IReadOnlyList<string> Uniforms);

public interface IVisualShaderProbe
{
    Task<VisualShaderProbeResult> ProbeAsync(
        string shaderPath,
        CancellationToken cancellationToken = default);
}
