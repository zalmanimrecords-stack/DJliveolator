using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class BindingMatcherTests
{
    private static ControllerBinding Binding(MidiMessageType trigger, int channel, int data1)
        => new(trigger, channel, data1, PerformanceActionKind.VisualBlackout, ActionInputMode.Momentary);

    [Fact]
    public void EffectiveType_TreatsNoteOnVelocity0AsNoteOff()
    {
        var message = new MidiMessage(MidiMessageType.NoteOn, 0, 36, 0);
        Assert.Equal(MidiMessageType.NoteOff, BindingMatcher.EffectiveType(message));
    }

    [Fact]
    public void Matches_NoteOnVelocity0_MatchesNoteOffBinding()
    {
        var binding = Binding(MidiMessageType.NoteOff, 0, 36);
        var message = new MidiMessage(MidiMessageType.NoteOn, 0, 36, 0);

        Assert.True(BindingMatcher.Matches(binding, message));
    }

    [Fact]
    public void Matches_RequiresSameChannel()
    {
        var binding = Binding(MidiMessageType.NoteOn, 0, 36);
        var message = new MidiMessage(MidiMessageType.NoteOn, 1, 36, 100);

        Assert.False(BindingMatcher.Matches(binding, message));
    }

    [Fact]
    public void Matches_RequiresSameData1ForCc()
    {
        var binding = Binding(MidiMessageType.ControlChange, 0, 10);
        var message = new MidiMessage(MidiMessageType.ControlChange, 0, 11, 100);

        Assert.False(BindingMatcher.Matches(binding, message));
    }

    [Fact]
    public void Matches_PitchBend_IgnoresData1()
    {
        var binding = Binding(MidiMessageType.PitchBend, 0, 0);
        var message = new MidiMessage(MidiMessageType.PitchBend, 0, 99, 64);

        Assert.True(BindingMatcher.Matches(binding, message));
    }
}
