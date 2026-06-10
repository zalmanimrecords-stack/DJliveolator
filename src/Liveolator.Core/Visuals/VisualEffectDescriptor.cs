namespace Liveolator.Core.Visuals;

public sealed record VisualEffectParameter(
    string Id,
    string Uniform,
    double Min,
    double Max,
    double Default);

/// <summary>How a GLSL fragment shader is used by the compositor (doc 26).</summary>
public enum VisualEffectRole
{
    /// <summary>
    /// A post-process effect: samples the layer's existing texture (<c>uTexture</c>) and transforms it.
    /// This is the default so older <c>visual-effects.json</c> without a role still load as effects.
    /// </summary>
    Effect = 0,

    /// <summary>
    /// A generator: draws the layer's pixels from uniforms alone (no input texture). Referenced as a
    /// layer source via <see cref="VisualSourceKind.Generator"/> — e.g. a VU meter.
    /// </summary>
    Generator = 1,
}

/// <summary>Validated metadata for a GLSL fragment shader supplied by an extension package.</summary>
public sealed record VisualEffectDescriptor(
    string EffectId,
    string Version,
    string PackageId,
    string ShaderPath,
    IReadOnlyList<VisualEffectParameter> Parameters,
    VisualEffectRole Role = VisualEffectRole.Effect,
    int MinimumOpenGlMajor = 3,
    int MinimumOpenGlMinor = 3,
    string? BackgroundImagePath = null);

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
