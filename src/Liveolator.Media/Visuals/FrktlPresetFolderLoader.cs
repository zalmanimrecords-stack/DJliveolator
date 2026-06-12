using System.Text.Json;
using Liveolator.Core.Visuals;

namespace Liveolator.Media.Visuals;

/// <summary>
/// Loads user-authored <c>.frktl</c> presets from a folder (doc 29) and registers each as a generator
/// effect + a controllable preset. Every file is self-contained (its own shader + up to five controllable
/// parameters); the loader validates it, extracts the shader to a cache <c>.frag</c> the compositor reads,
/// and registers all valid presets atomically under one package id.
/// </summary>
/// <remarks>
/// Tolerant by design (doc 21): a missing folder yields zero presets, and a malformed or invalid file is
/// skipped + reported via <c>onWarning</c> rather than aborting the rest (global standards #16/#26).
/// </remarks>
public sealed class FrktlPresetFolderLoader : IVisualPresetReloader
{
    /// <summary>The package id all folder presets are registered under (kept apart from built-ins).</summary>
    public const string PackageId = "liveolator.frktl.user";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IVisualEffectRegistry _effects;
    private readonly IGeneratorPresetRegistry _presets;
    private readonly string _cacheFolder;
    private readonly Action<string>? _onWarning;

    public FrktlPresetFolderLoader(
        IVisualEffectRegistry effects,
        IGeneratorPresetRegistry presets,
        string? folder = null,
        Action<string>? onWarning = null)
    {
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _presets = presets ?? throw new ArgumentNullException(nameof(presets));
        Folder = folder ?? Path.Combine(JsonCatalogStore.DefaultRoot(), "frktl-presets");
        _cacheFolder = Path.Combine(Folder, ".cache");
        _onWarning = onWarning;
    }

    /// <summary>The folder scanned for <c>*.frktl</c> files (created on demand by <see cref="Load"/>).</summary>
    public string Folder { get; }

    /// <summary>
    /// Scans the folder, validates and compiles every <c>.frktl</c>, and replaces the package's registered
    /// effects + presets with the result. Returns the number of presets registered. Never throws.
    /// </summary>
    public int Load()
    {
        var descriptors = new List<VisualEffectDescriptor>();
        var presets = new List<GeneratorPreset>();
        var seenEffectIds = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            Directory.CreateDirectory(Folder);
            foreach (string path in Directory
                         .EnumerateFiles(Folder, "*.frktl", SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                TryLoadOne(path, descriptors, presets, seenEffectIds);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"FRKTL preset folder '{Folder}' could not be scanned ({ex.Message}).");
        }

        // Replace as a unit so a reload reflects added/removed files (and clears stale registrations).
        _effects.ReplacePackage(PackageId, descriptors);
        _presets.ReplacePackage(PackageId, presets);
        return presets.Count;
    }

    /// <summary>
    /// Runtime re-scan (doc 29): identical to <see cref="Load"/>, exposed through <see cref="IVisualPresetReloader"/>
    /// so the LIVE surface can refresh presets authored while the app is running without a restart.
    /// </summary>
    public int Reload() => Load();

    private void TryLoadOne(
        string path,
        List<VisualEffectDescriptor> descriptors,
        List<GeneratorPreset> presets,
        HashSet<string> seenEffectIds)
    {
        string fileName = Path.GetFileName(path);
        try
        {
            FrktlPresetFile? file = JsonSerializer.Deserialize<FrktlPresetFile>(File.ReadAllText(path), JsonOptions);
            FrktlPresetValidation validation = FrktlPresetValidator.Validate(file);
            if (!validation.IsValid)
            {
                _onWarning?.Invoke($"FRKTL preset '{fileName}' was skipped ({validation.Error}).");
                return;
            }

            string slug = FrktlPresetNaming.Slug(Path.GetFileNameWithoutExtension(path));
            string effectId = $"{PackageId}/{slug}";
            if (!seenEffectIds.Add(effectId))
            {
                _onWarning?.Invoke($"FRKTL preset '{fileName}' was skipped (duplicate id '{effectId}').");
                return;
            }

            Directory.CreateDirectory(_cacheFolder);
            string shaderPath = Path.Combine(_cacheFolder, slug + ".frag");
            File.WriteAllText(shaderPath, file!.Shader);

            FrktlPresetCompiler.Compiled compiled = FrktlPresetCompiler.Compile(file, effectId, PackageId, shaderPath);
            descriptors.Add(compiled.Descriptor);
            presets.Add(compiled.Preset);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _onWarning?.Invoke($"FRKTL preset '{fileName}' was skipped ({ex.Message}).");
        }
    }
}
