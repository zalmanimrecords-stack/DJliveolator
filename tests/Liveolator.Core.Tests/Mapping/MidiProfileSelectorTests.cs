using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class MidiProfileSelectorTests
{
    private static ControllerMappingProfile Profile(string name, string hint)
        => ControllerMappingProfile.Empty(name, hint);

    [Fact]
    public void Select_MatchesHintAsCaseInsensitiveSubstring()
    {
        var push = Profile("Push", "Ableton Push");
        var cmd = Profile("CMD", "CMD Studio");

        ControllerMappingProfile? selected = MidiProfileSelector.Select(
            "ABLETON PUSH 1 - User Port", new[] { cmd, push });

        Assert.Same(push, selected);
    }

    [Fact]
    public void Select_ReturnsNull_WhenNoHintMatches()
    {
        ControllerMappingProfile? selected = MidiProfileSelector.Select(
            "Unknown Device", new[] { Profile("Push", "Ableton Push") });

        Assert.Null(selected);
    }

    [Fact]
    public void Select_IgnoresProfilesWithEmptyHint()
    {
        ControllerMappingProfile? selected = MidiProfileSelector.Select(
            "Any Device", new[] { Profile("Generic", string.Empty) });

        Assert.Null(selected);
    }

    [Fact]
    public void Select_ReturnsFirstMatch_WhenSeveralHintsApply()
    {
        var first = Profile("First", "Studio");
        var second = Profile("Second", "CMD Studio");

        ControllerMappingProfile? selected = MidiProfileSelector.Select(
            "CMD Studio 2A", new[] { first, second });

        Assert.Same(first, selected);
    }
}
