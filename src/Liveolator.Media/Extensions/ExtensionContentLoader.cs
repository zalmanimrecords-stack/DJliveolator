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
    private readonly Action<string>? _onWarning;

    public ExtensionContentLoader(
        IExtensionCatalog catalog,
        IVisualEffectRegistry effects,
        IUiThemeManager themes,
        IVisualShaderProbe? shaderProbe = null,
        Action<string>? onWarning = null)
    {
        _catalog = catalog;
        _effects = effects;
        _themes = themes;
        _shaderProbe = shaderProbe;
        _onWarning = onWarning;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        foreach (InstalledExtension extension in _catalog.Installed)
        {
            _effects.RemovePackage(extension.Manifest.PackageId);
            _themes.RemovePackage(extension.Manifest.PackageId);
            if (!extension.IsEnabled)
                continue;

            try
            {
                await LoadEffectsAsync(extension, cancellationToken).ConfigureAwait(false);
                await LoadThemesAsync(extension, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
            {
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
        }
        _effects.ReplacePackage(extension.Manifest.PackageId, descriptors);
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
