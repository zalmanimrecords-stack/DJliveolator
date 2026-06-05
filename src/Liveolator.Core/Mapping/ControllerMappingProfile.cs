namespace Liveolator.Core.Mapping;

/// <summary>
/// A named, device-targeted set of bindings. Serializes to JSON under the Live persistence root
/// (doc 13) and is import/export-friendly so performers can share profiles (doc 05).
/// </summary>
/// <param name="Name">Human-facing profile name.</param>
/// <param name="DeviceHint">Substring matched against a device name to auto-select this profile.</param>
/// <param name="Bindings">The control→action bindings.</param>
public sealed record ControllerMappingProfile(
    string Name,
    string DeviceHint,
    IReadOnlyList<ControllerBinding> Bindings)
{
    /// <summary>An empty profile, useful as a starting point for learn mode.</summary>
    public static ControllerMappingProfile Empty(string name, string deviceHint)
        => new(name, deviceHint, Array.Empty<ControllerBinding>());

    /// <summary>Returns a copy of this profile with <paramref name="binding"/> appended.</summary>
    public ControllerMappingProfile WithBinding(ControllerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return this with { Bindings = new List<ControllerBinding>(Bindings) { binding } };
    }
}
