namespace Liveolator.Core.Mapping;

/// <summary>
/// Finds bindings that share the same physical trigger within a profile. Pure and stateless so it
/// runs at profile-edit time without touching a device (doc 05).
/// </summary>
public static class MappingConflictDetector
{
    private const int PitchBendAddress = -1; // pitch bend has no note/CC address; group by channel only

    /// <summary>Returns one <see cref="MappingConflict"/> per group of two or more colliding bindings.</summary>
    public static IReadOnlyList<MappingConflict> Detect(ControllerMappingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Bindings
            .GroupBy(Key)
            .Where(group => group.Count() > 1)
            .Select(group => new MappingConflict(
                group.Key.Type, group.Key.Channel, group.Key.Data1, group.ToList()))
            .ToList();
    }

    private static (MidiMessageType Type, int Channel, int Data1) Key(ControllerBinding binding)
    {
        int address = binding.TriggerType == MidiMessageType.PitchBend ? PitchBendAddress : binding.Data1;
        return (binding.TriggerType, binding.Channel, address);
    }
}
