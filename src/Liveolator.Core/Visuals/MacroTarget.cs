namespace Liveolator.Core.Visuals;

using System.Text.Json.Serialization;

/// <summary>
/// Structured address for a layer property or a parameter on a stable visual-effect instance.
/// </summary>
public sealed record MacroTarget
{
    public MacroTarget(int Layer, string Parameter)
        : this(Layer, EffectInstanceId: null, Parameter)
    {
    }

    [JsonConstructor]
    public MacroTarget(int Layer, string? EffectInstanceId, string Parameter)
    {
        if (Layer < 0)
            throw new ArgumentOutOfRangeException(nameof(Layer));
        if (string.IsNullOrWhiteSpace(Parameter))
            throw new ArgumentException("Parameter is required.", nameof(Parameter));

        this.Layer = Layer;
        this.EffectInstanceId = string.IsNullOrWhiteSpace(EffectInstanceId) ? null : EffectInstanceId;
        this.Parameter = Parameter;
    }

    public int Layer { get; init; }
    public string? EffectInstanceId { get; init; }
    public string Parameter { get; init; }
}
