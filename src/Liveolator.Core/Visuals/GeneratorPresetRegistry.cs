namespace Liveolator.Core.Visuals;

/// <summary>
/// Thread-safe in-memory <see cref="IGeneratorPresetRegistry"/> (doc 28). Packages are stored
/// separately and republished as a flat, immutable snapshot on every change so the render thread reads
/// a consistent list without locking — the same pattern as <see cref="VisualEffectRegistry"/>.
/// </summary>
public sealed class GeneratorPresetRegistry : IGeneratorPresetRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GeneratorPreset[]> _packages = new(StringComparer.Ordinal);
    private GeneratorPreset[] _presets = Array.Empty<GeneratorPreset>();

    public IReadOnlyList<GeneratorPreset> Presets => Volatile.Read(ref _presets);

    public bool TryGet(string presetId, out GeneratorPreset preset)
    {
        preset = Presets.FirstOrDefault(p => string.Equals(p.PresetId, presetId, StringComparison.Ordinal))!;
        return preset is not null;
    }

    public void ReplacePackage(string packageId, IEnumerable<GeneratorPreset> presets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(presets);
        lock (_gate)
        {
            GeneratorPreset[] next = presets.ToArray();
            // Validate the prospective combined set before mutating, so a rejected replace (e.g. a
            // duplicate preset id) leaves the registry exactly as it was.
            IEnumerable<GeneratorPreset> prospective = _packages
                .Where(entry => !string.Equals(entry.Key, packageId, StringComparison.Ordinal))
                .SelectMany(entry => entry.Value)
                .Concat(next);
            ValidateUnique(prospective);

            _packages[packageId] = next;
            Publish();
        }
    }

    public void RemovePackage(string packageId)
    {
        lock (_gate)
        {
            _packages.Remove(packageId);
            Publish();
        }
    }

    private void Publish()
    {
        GeneratorPreset[] all = _packages.Values.SelectMany(p => p).ToArray();
        ValidateUnique(all);
        Volatile.Write(ref _presets, all);
    }

    private static void ValidateUnique(IEnumerable<GeneratorPreset> presets)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (GeneratorPreset preset in presets)
        {
            if (!ids.Add(preset.PresetId))
                throw new InvalidOperationException($"Generator preset '{preset.PresetId}' is registered twice.");
        }
    }
}
