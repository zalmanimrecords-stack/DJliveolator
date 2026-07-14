namespace Liveolator.Core.Mapping;

/// <summary>
/// Picks the mapping profile whose <see cref="ControllerMappingProfile.DeviceHint"/> matches a
/// connected device name, so plugging in a known controller loads its profile automatically
/// (doc 05). Pure and case-insensitive.
/// </summary>
public static class MidiProfileSelector
{
    /// <summary>
    /// Returns the first profile whose non-empty hint is a substring of <paramref name="deviceName"/>,
    /// or null when none match. Order of <paramref name="profiles"/> is the tie-break.
    /// </summary>
    public static ControllerMappingProfile? Select(
        string deviceName, IEnumerable<ControllerMappingProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(deviceName);
        ArgumentNullException.ThrowIfNull(profiles);

        foreach (ControllerMappingProfile profile in profiles)
        {
            if (!string.IsNullOrEmpty(profile.DeviceHint)
                && deviceName.Contains(profile.DeviceHint, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }
}
