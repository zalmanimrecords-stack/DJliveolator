using Liveolator.Core.Mapping;
using Liveolator.Core.Mapping.Profiles;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>
/// The generic template profile is the learn-from-scratch starting point for ANY controller without a
/// device-specific default (doc 05). It must be named, empty, and have a blank DeviceHint so it never
/// auto-matches a device — only explicit device profiles win <see cref="MidiProfileSelector"/>.
/// </summary>
public class GenericControllerProfileTests
{
    [Fact]
    public void Default_IsNamed_Empty_AndHasNoDeviceHint()
    {
        ControllerMappingProfile generic = GenericControllerProfile.Default;

        Assert.Equal(GenericControllerProfile.ProfileName, generic.Name);
        Assert.Equal(string.Empty, generic.DeviceHint);
        Assert.Empty(generic.Bindings);
    }

    [Fact]
    public void Generic_NeverAutoMatches_EvenLastInTheCatalog()
    {
        // The catalog orders device-specific profiles first; the empty-hint generic must never win, so an
        // unknown device resolves to null (and the empty template is used only as an explicit fallback).
        var known = ControllerMappingProfile.Empty("CMD", "CMD Studio");

        ControllerMappingProfile? selected = MidiProfileSelector.Select(
            "any random device", new[] { known, GenericControllerProfile.Default });

        Assert.Null(selected);
    }
}
