namespace Liveolator.Core.Visuals;

/// <summary>
/// One generator parameter that a <see cref="GeneratorPreset"/> exposes as a live, externally
/// controllable knob. <see cref="Id"/> references a <see cref="VisualEffectParameter.Id"/> on the
/// preset's generator descriptor; <see cref="Label"/> is the caption shown on the UI knob (for
/// example "GLOW"). A preset may expose at most <see cref="GeneratorPreset.MaxControllableParameters"/>.
/// </summary>
public sealed record ControllableParameter
{
    public ControllableParameter(string id, string label)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Controllable parameter id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Controllable parameter label is required.", nameof(label));

        Id = id;
        Label = label;
    }

    public string Id { get; init; }
    public string Label { get; init; }
}
