using Liveolator.Core.Visuals;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Shared registration plumbing for the built-in controllable preset add-ons (doc 28): emits the
/// generator shader into the visual asset cache (rewriting when stale so a shipped update reaches an
/// existing install) and registers effect + preset under one package id so uninstall/reload removes
/// them together. A write failure degrades to a warning and leaves the registries untouched — never
/// crashes composition (doc 08 rule).
/// </summary>
internal static class BuiltInPresetAddonRegistration
{
    public static string EnsureShaderCreated(string fileName, string fragmentShader, string? directory = null)
    {
        directory ??= VisualAssetPaths.Default();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path) || File.ReadAllText(path) != fragmentShader)
            File.WriteAllText(path, fragmentShader);
        return path;
    }

    public static bool TryRegister(
        string packageId,
        string presetName,
        string shaderFileName,
        string fragmentShader,
        Func<string, VisualEffectDescriptor> descriptor,
        GeneratorPreset preset,
        IVisualEffectRegistry effects,
        IGeneratorPresetRegistry presets,
        Action<string>? onWarning)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(presets);
        try
        {
            string shaderPath = EnsureShaderCreated(shaderFileName, fragmentShader);
            effects.ReplacePackage(packageId, new[] { descriptor(shaderPath) });
            presets.ReplacePackage(packageId, new[] { preset });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            onWarning?.Invoke($"Built-in {presetName} preset unavailable ({ex.Message}).");
            return false;
        }
    }
}
