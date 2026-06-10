using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Extensions;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;

namespace Liveolator.Media.Extensions;

/// <summary>Loads declarative content from enabled, already-validated extension directories.</summary>
public sealed class ExtensionContentLoader : IExtensionContentReloader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IExtensionCatalog _catalog;
    private readonly IVisualEffectRegistry _effects;
    private readonly IUiThemeManager _themes;
    private readonly IVisualShaderProbe? _shaderProbe;
    private readonly IGeneratorPresetRegistry? _presets;
    private readonly Action<string>? _onWarning;

    public ExtensionContentLoader(
        IExtensionCatalog catalog,
        IVisualEffectRegistry effects,
        IUiThemeManager themes,
        IVisualShaderProbe? shaderProbe = null,
        Action<string>? onWarning = null,
        IGeneratorPresetRegistry? presets = null)
    {
        _catalog = catalog;
        _effects = effects;
        _themes = themes;
        _shaderProbe = shaderProbe;
        _onWarning = onWarning;
        _presets = presets;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        foreach (InstalledExtension extension in _catalog.Installed)
        {
            _effects.RemovePackage(extension.Manifest.PackageId);
            _themes.RemovePackage(extension.Manifest.PackageId);
            _presets?.RemovePackage(extension.Manifest.PackageId);
            if (!extension.IsEnabled)
                continue;

            try
            {
                await LoadEffectsAsync(extension, cancellationToken).ConfigureAwait(false);
                await LoadThemesAsync(extension, cancellationToken).ConfigureAwait(false);
                // Presets resolve against the effects just registered above, so load them last.
                await LoadPresetsAsync(extension, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException
                or InvalidDataException)
            {
                // InvalidDataException (wrong package id, too many params, a shader missing a declared
                // uniform, probe rejection) is NOT an IOException, so it must be listed explicitly —
                // otherwise one malformed third-party pack would abort loading every pack and could throw
                // at startup (ReloadAsync runs in the composition root). One bad pack is skipped + logged,
                // the rest still load (doc 21 tolerance; global standards #16/#26).
                _onWarning?.Invoke(
                    $"Extension content for '{extension.Manifest.PackageId}' was ignored ({ex.Message}).");
            }
        }
    }

    private async Task LoadEffectsAsync(InstalledExtension extension, CancellationToken cancellationToken)
    {
        string path = Path.Combine(extension.InstallPath, "visual-effects.json");
        if (!File.Exists(path))
            return;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        VisualEffectDescriptor[] descriptors =
            await JsonSerializer.DeserializeAsync<VisualEffectDescriptor[]>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false)
            ?? Array.Empty<VisualEffectDescriptor>();

        var resolvedDescriptors = new List<VisualEffectDescriptor>(descriptors.Length);
        foreach (VisualEffectDescriptor descriptor in descriptors)
        {
            if (!string.Equals(descriptor.PackageId, extension.Manifest.PackageId, StringComparison.Ordinal))
                throw new InvalidDataException($"Effect '{descriptor.EffectId}' declares the wrong package id.");
            if (descriptor.Parameters.Count > 64)
                throw new InvalidDataException($"Effect '{descriptor.EffectId}' declares too many parameters.");
            string shader = ResolveContentPath(extension.InstallPath, descriptor.ShaderPath);
            if (!File.Exists(shader))
                throw new FileNotFoundException($"Shader for '{descriptor.EffectId}' is missing.", shader);
            if (_shaderProbe is not null)
            {
                VisualShaderProbeResult result = await _shaderProbe.ProbeAsync(shader, cancellationToken)
                    .ConfigureAwait(false);
                if (!result.IsValid)
                    throw new InvalidDataException(
                        $"Shader for '{descriptor.EffectId}' failed isolated validation: {result.Error}");
                string[] declaredUniforms = descriptor.Parameters.Select(p => p.Uniform).ToArray();
                if (declaredUniforms.Any(uniform => !result.Uniforms.Contains(uniform, StringComparer.Ordinal)))
                    throw new InvalidDataException(
                        $"Shader for '{descriptor.EffectId}' is missing a declared uniform.");
            }
            resolvedDescriptors.Add(descriptor with { ShaderPath = shader });
        }
        _effects.ReplacePackage(extension.Manifest.PackageId, resolvedDescriptors);
    }

    private async Task LoadThemesAsync(InstalledExtension extension, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(extension.InstallPath, "themes");
        if (!Directory.Exists(directory))
            return;
        var themes = new List<UiThemeDefinition>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            UiThemeDefinition? theme = await JsonSerializer.DeserializeAsync<UiThemeDefinition>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (theme is not null)
                themes.Add(theme);
        }
        _themes.ReplacePackage(extension.Manifest.PackageId, themes);
    }

    private async Task LoadPresetsAsync(InstalledExtension extension, CancellationToken cancellationToken)
    {
        if (_presets is null)
            return;
        string path = Path.Combine(extension.InstallPath, "presets.json");
        if (!File.Exists(path))
            return;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        GeneratorPreset[] presets =
            await JsonSerializer.DeserializeAsync<GeneratorPreset[]>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false)
            ?? Array.Empty<GeneratorPreset>();

        foreach (GeneratorPreset preset in presets)
        {
            // The generator the preset wraps must be a registered Generator-role effect (its own package's
            // effects are already registered, or a dependency's), and every controllable parameter must be
            // one the generator actually declares — otherwise the knob would drive a uniform that does not
            // exist. A violation throws InvalidDataException, so the caller skips this pack's presets + logs.
            if (!_effects.TryGet(preset.GeneratorEffectId, preset.GeneratorVersion, out VisualEffectDescriptor generator))
                throw new InvalidDataException(
                    $"Preset '{preset.PresetId}' references unknown generator '{preset.GeneratorEffectId}' ({preset.GeneratorVersion}).");
            if (generator.Role != VisualEffectRole.Generator)
                throw new InvalidDataException(
                    $"Preset '{preset.PresetId}' references '{preset.GeneratorEffectId}', which is not a generator.");

            var declared = new HashSet<string>(generator.Parameters.Select(p => p.Id), StringComparer.Ordinal);
            foreach (ControllableParameter controllable in preset.Controllable)
            {
                if (!declared.Contains(controllable.Id))
                    throw new InvalidDataException(
                        $"Preset '{preset.PresetId}' exposes '{controllable.Id}', which generator '{preset.GeneratorEffectId}' does not declare.");
            }
        }

        _presets.ReplacePackage(extension.Manifest.PackageId, presets);
    }

    private static string ResolveContentPath(string root, string relative)
    {
        string? normalized = ExtensionPackageValidator.NormalizeEntryPath(relative);
        if (normalized is null)
            throw new InvalidDataException($"Unsafe extension content path '{relative}'.");
        string full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        string rootPrefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Extension content path '{relative}' escapes its package.");
        return full;
    }
}
