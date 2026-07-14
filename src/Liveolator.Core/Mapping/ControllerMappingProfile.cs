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
    /// <summary>
    /// Optional raw SysEx sent to the device's output the moment this profile connects — e.g. switching
    /// an Ableton Push into User mode so its pads/encoders emit MIDI at all (doc 06). Null/empty = nothing
    /// sent. Requires the device's output to be open (the same device as a feedback output).
    /// </summary>
    public IReadOnlyList<byte>? ActivationSysEx { get; init; }

    /// <summary>Optional raw SysEx sent when the profile disconnects — e.g. returning the Push to Live mode.</summary>
    public IReadOnlyList<byte>? DeactivationSysEx { get; init; }

    /// <summary>
    /// When true, LED feedback uses a colour-palette index as the note velocity (active = lit colour,
    /// available = dim, off = 0) instead of plain on/off — for devices whose pads are colour-addressed
    /// by velocity, like the Ableton Push (doc 06). Off by default (single-colour button LEDs).
    /// </summary>
    public bool UsesColorFeedback { get; init; }

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
