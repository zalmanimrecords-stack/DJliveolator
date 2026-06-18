namespace Liveolator.Core.Mapping.Profiles;

/// <summary>
/// A blank, learn-from-scratch template <see cref="ControllerMappingProfile"/> for ANY connected MIDI
/// controller that has no device-specific default profile (doc 05). It ships zero bindings, so a
/// performer plugging in an arbitrary controller gets a labelled starting point and learns each control
/// via <see cref="MidiLearnSession"/> from the Mappings tab.
/// </summary>
/// <remarks>
/// The <see cref="ControllerMappingProfile.DeviceHint"/> is intentionally EMPTY: a blank hint never
/// matches a device name in <see cref="MidiProfileSelector"/>, so this template can never auto-select
/// over a real device-specific profile (CMD STUDIO 2A, DDJ-FLX4, ...). It is a label/template only.
/// </remarks>
public static class GenericControllerProfile
{
    /// <summary>The profile name shown in the Mappings UI for an unrecognized controller.</summary>
    public const string ProfileName = "Generic MIDI Controller";

    /// <summary>An empty, never-auto-matching template profile for an arbitrary MIDI controller.</summary>
    public static ControllerMappingProfile Default { get; } =
        ControllerMappingProfile.Empty(ProfileName, deviceHint: string.Empty);
}
