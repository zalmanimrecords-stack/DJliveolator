using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Tests.Actions;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class MidiFeedbackPublisherTests
{
    private readonly RecordingDispatcher _dispatcher = new();
    private readonly FakeMidiOutput _output = new();

    private MidiFeedbackPublisher Build(params ControllerBinding[] bindings)
    {
        var mapper = new ControllerMapper(
            new ControllerMappingProfile("p", "device", bindings), _dispatcher, new CapturingLogger<ControllerMapper>());
        return new MidiFeedbackPublisher(_dispatcher, _output, mapper, new CapturingLogger<MidiFeedbackPublisher>());
    }

    private MidiFeedbackPublisher BuildColor(params ControllerBinding[] bindings)
    {
        var profile = new ControllerMappingProfile("push", "Push", bindings) { UsesColorFeedback = true };
        var mapper = new ControllerMapper(profile, _dispatcher, new CapturingLogger<ControllerMapper>());
        return new MidiFeedbackPublisher(_dispatcher, _output, mapper, new CapturingLogger<MidiFeedbackPublisher>());
    }

    private static ControllerBinding ToggleBinding => new(
        MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.BeatLock, ActionInputMode.Toggle);

    [Fact]
    public void ColorFeedback_LightsPaletteColours_ForLitDimAndOff()
    {
        // A colour-addressed device (Push) sets the pad's velocity to a palette colour, not on/off.
        using var publisher = BuildColor(ToggleBinding);

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.BeatLock, 0, new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0)));
        Assert.Equal(122, _output.Sent[^1].Data2);  // lit colour, not the binary 127
        Assert.NotEqual(127, _output.Sent[^1].Data2);

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.BeatLock, 0, new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0)));
        Assert.Equal(1, _output.Sent[^1].Data2);     // dim (available but inactive)

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.BeatLock, 0, new ActionFeedbackState(IsActive: false, IsAvailable: false, Value: 0)));
        Assert.Equal(0, _output.Sent[^1].Data2);     // off
    }

    [Fact]
    public void ActiveToggle_LightsTheBoundPadFullOn()
    {
        using var publisher = Build(ToggleBinding);

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.BeatLock, 0, new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0)));

        MidiMessage sent = Assert.Single(_output.Sent);
        Assert.Equal(MidiMessageType.NoteOn, sent.Type);
        Assert.Equal(36, sent.Data1);
        Assert.Equal(127, sent.Data2);
    }

    [Fact]
    public void InactiveToggle_TurnsThePadOff()
    {
        using var publisher = Build(ToggleBinding);

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.BeatLock, 0, new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0)));

        Assert.Equal(0, Assert.Single(_output.Sent).Data2);
    }

    [Fact]
    public void AbsoluteBinding_ScalesValueTo7Bit()
    {
        var fader = new ControllerBinding(
            MidiMessageType.ControlChange, 0, 7, PerformanceActionKind.MixerChannelGain, ActionInputMode.Absolute);
        using var publisher = Build(fader);

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.MixerChannelGain, 0, new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0.5)));

        MidiMessage sent = Assert.Single(_output.Sent);
        Assert.Equal(MidiMessageType.ControlChange, sent.Type);
        Assert.Equal(64, sent.Data2); // round(0.5 * 127)
    }

    [Fact]
    public void Feedback_ForActionWithNoBinding_SendsNothing()
    {
        using var publisher = Build(ToggleBinding);

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.VisualBlackout, 0, new ActionFeedbackState(true, true, 0)));

        Assert.Empty(_output.Sent);
    }

    [Fact]
    public void Feedback_RespectsSlot()
    {
        var slot1 = new ControllerBinding(
            MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualLaunchClip, ActionInputMode.Momentary, Slot: 1);
        using var publisher = Build(slot1);

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.VisualLaunchClip, 0, new ActionFeedbackState(true, true, 0)));

        Assert.Empty(_output.Sent); // slot 0 feedback must not light the slot-1 pad
    }

    [Fact]
    public void PitchBendBinding_IsNotAnLedTarget()
    {
        var bend = new ControllerBinding(
            MidiMessageType.PitchBend, 0, 0, PerformanceActionKind.BeatNudgeForward, ActionInputMode.Absolute);
        using var publisher = Build(bend);

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.BeatNudgeForward, 0, new ActionFeedbackState(true, true, 1)));

        Assert.Empty(_output.Sent);
    }

    [Fact]
    public void Dispose_StopsPublishingFeedback()
    {
        var publisher = Build(ToggleBinding);
        publisher.Dispose();

        _dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.BeatLock, 0, new ActionFeedbackState(true, true, 0)));

        Assert.Empty(_output.Sent);
    }
}
