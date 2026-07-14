using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class MappingConflictDetectorTests
{
    private static ControllerBinding Binding(MidiMessageType trigger, int channel, int data1, PerformanceActionKind action)
        => new(trigger, channel, data1, action, ActionInputMode.Momentary);

    private static ControllerMappingProfile Profile(params ControllerBinding[] bindings)
        => new("p", "device", bindings);

    [Fact]
    public void Detect_FlagsTwoBindingsOnSameTrigger()
    {
        var profile = Profile(
            Binding(MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualBlackout),
            Binding(MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.BeatLock));

        MappingConflict conflict = Assert.Single(MappingConflictDetector.Detect(profile));

        Assert.Equal(36, conflict.Data1);
        Assert.Equal(2, conflict.Bindings.Count);
    }

    [Fact]
    public void Detect_DifferentData1_NoConflict()
    {
        var profile = Profile(
            Binding(MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualBlackout),
            Binding(MidiMessageType.NoteOn, 0, 37, PerformanceActionKind.BeatLock));

        Assert.Empty(MappingConflictDetector.Detect(profile));
    }

    [Fact]
    public void Detect_PitchBendOnSameChannel_ConflictsRegardlessOfData1()
    {
        var profile = Profile(
            Binding(MidiMessageType.PitchBend, 0, 0, PerformanceActionKind.MixerCrossfade),
            Binding(MidiMessageType.PitchBend, 0, 99, PerformanceActionKind.BeatNudgeForward));

        MappingConflict conflict = Assert.Single(MappingConflictDetector.Detect(profile));
        Assert.Equal(-1, conflict.Data1);
    }

    [Fact]
    public void Detect_EmptyProfile_NoConflicts()
        => Assert.Empty(MappingConflictDetector.Detect(ControllerMappingProfile.Empty("p", "device")));
}
