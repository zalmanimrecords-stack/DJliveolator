namespace Liveolator.Core.Visuals;

/// <summary>
/// Discovery and resolution of <see cref="GeneratorPreset"/>s (doc 28), mirroring
/// <see cref="IVisualEffectRegistry"/>: presets are owned by a package and replaced/removed as a unit
/// so an extension install/uninstall is atomic. Preset ids are unique across all packages.
/// </summary>
public interface IGeneratorPresetRegistry
{
    IReadOnlyList<GeneratorPreset> Presets { get; }

    bool TryGet(string presetId, out GeneratorPreset preset);

    void ReplacePackage(string packageId, IEnumerable<GeneratorPreset> presets);

    void RemovePackage(string packageId);
}
