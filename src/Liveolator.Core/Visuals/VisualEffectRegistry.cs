namespace Liveolator.Core.Visuals;

public sealed class VisualEffectRegistry : IVisualEffectRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, VisualEffectDescriptor[]> _packages = new(StringComparer.Ordinal);
    private VisualEffectDescriptor[] _effects = Array.Empty<VisualEffectDescriptor>();

    public IReadOnlyList<VisualEffectDescriptor> Effects => Volatile.Read(ref _effects);

    public bool TryGet(string effectId, string? version, out VisualEffectDescriptor descriptor)
    {
        descriptor = Effects.FirstOrDefault(e =>
            string.Equals(e.EffectId, effectId, StringComparison.Ordinal)
            && (version is null || string.Equals(e.Version, version, StringComparison.Ordinal)))!;
        return descriptor is not null;
    }

    public void ReplacePackage(string packageId, IEnumerable<VisualEffectDescriptor> effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(effects);
        lock (_gate)
        {
            VisualEffectDescriptor[] next = effects.ToArray();
            if (next.Any(e => !string.Equals(e.PackageId, packageId, StringComparison.Ordinal)))
                throw new ArgumentException("Every effect must belong to the replaced package.", nameof(effects));
            ValidateUnique(next);
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
        VisualEffectDescriptor[] all = _packages.Values.SelectMany(e => e).ToArray();
        ValidateUnique(all);
        Volatile.Write(ref _effects, all);
    }

    private static void ValidateUnique(IEnumerable<VisualEffectDescriptor> effects)
    {
        var ids = new HashSet<(string Id, string Version)>();
        foreach (VisualEffectDescriptor effect in effects)
        {
            if (!ids.Add((effect.EffectId, effect.Version)))
                throw new InvalidOperationException(
                    $"Visual effect '{effect.EffectId}' version '{effect.Version}' is registered twice.");
        }
    }
}
