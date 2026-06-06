namespace Liveolator.Core.Visuals;

using System.Text.Json.Serialization;

/// <summary>
/// Serializable reference to one visual-effect instance in a layer chain. Effect ids are
/// package-qualified (for example <c>com.liveolator.core/brightness</c>); the instance id stays
/// stable so macros and saved shows do not depend on chain position.
/// </summary>
public sealed record EffectRef
{
    public EffectRef(string effectId, IReadOnlyDictionary<string, double> defaults)
        : this(effectId, "1.0.0", Guid.NewGuid().ToString("N"), defaults)
    {
    }

    [JsonConstructor]
    public EffectRef(
        string EffectId,
        string? Version,
        string? InstanceId,
        IReadOnlyDictionary<string, double> Defaults)
    {
        if (string.IsNullOrWhiteSpace(EffectId))
            throw new ArgumentException("Effect id is required.", nameof(EffectId));

        this.EffectId = EffectId;
        this.Version = string.IsNullOrWhiteSpace(Version) ? "1.0.0" : Version;
        this.InstanceId = string.IsNullOrWhiteSpace(InstanceId)
            ? Guid.NewGuid().ToString("N")
            : InstanceId;
        this.Defaults = Defaults ?? throw new ArgumentNullException(nameof(Defaults));
    }

    public string EffectId { get; init; }
    public string Version { get; init; }
    public string InstanceId { get; init; }
    public IReadOnlyDictionary<string, double> Defaults { get; init; }
}
