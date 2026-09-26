using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Tests.Actions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

public class ControllerMapperTests
{
    private readonly RecordingDispatcher _dispatcher = new();
    private readonly CapturingLogger<ControllerMapper> _logger = new();

    private ControllerMapper Build(params ControllerBinding[] bindings)
        => new(new ControllerMappingProfile("p", "device", bindings), _dispatcher, _logger);

    [Fact]
    public void Apply_DispatchesActionForMatchingBinding()
    {
        var binding = new ControllerBinding(
            MidiMessageType.ControlChange, 0, 10, PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, Slot: 1);
        var mapper = Build(binding);

        mapper.Apply(new MidiMessage(MidiMessageType.ControlChange, 0, 10, 127));

        PerformanceAction action = Assert.Single(_dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerCrossfade, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(1, action.Slot);
        Assert.Equal(1.0, action.Value, precision: 6);
    }

    [Fact]
    public void Apply_AbsoluteControl_AfterAutomationMovedItsTarget_HoldsUntilTheHardwareReachesIt()
    {
        var mapper = Build(new ControllerBinding(
            MidiMessageType.ControlChange, 0, 10, PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute));
        _dispatcher.FeedbackValue = 1.0;           // an AUTO fade left the crossfader on B
        _dispatcher.FeedbackRequiresPickup = true; // ...while the physical fader still sits on A

        mapper.Apply(new MidiMessage(MidiMessageType.ControlChange, 0, 10, 0));
        mapper.Apply(new MidiMessage(MidiMessageType.ControlChange, 0, 10, 64));
        Assert.Empty(_dispatcher.Dispatched);      // the first touch does not jump the mix back to A

        mapper.Apply(new MidiMessage(MidiMessageType.ControlChange, 0, 10, 127));
        Assert.Equal(1.0, Assert.Single(_dispatcher.Dispatched).Value, precision: 6);
    }

    [Fact]
    public void Apply_AbsoluteControl_OtherThanTheCrossfader_NeverAsksForFeedback()
    {
        // Only AUTO moves a target out from under the hardware, and only the crossfader. Asking on every other
        // knob tick is wasted work on the MIDI path — on macOS a system-volume query spawns osascript.
        var mapper = Build(new ControllerBinding(
            MidiMessageType.ControlChange, 0, 11, PerformanceActionKind.SystemMasterVolume, ActionInputMode.Absolute));
        _dispatcher.FeedbackRequiresPickup = true;

        mapper.Apply(new MidiMessage(MidiMessageType.ControlChange, 0, 11, 64));

        Assert.Single(_dispatcher.Dispatched);
        Assert.Equal(0, _dispatcher.FeedbackQueries);
    }

    [Fact]
    public void Apply_NoMatchingBinding_DispatchesNothing()
    {
        var mapper = Build(new ControllerBinding(
            MidiMessageType.ControlChange, 0, 10, PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute));

        mapper.Apply(new MidiMessage(MidiMessageType.ControlChange, 0, 99, 127));

        Assert.Empty(_dispatcher.Dispatched);
    }

    [Fact]
    public void Apply_MomentaryNote_DispatchesPressOnly_AndIsPressedTrue()
    {
        // A normal momentary button: NoteOn fires (pressed), NoteOff is dropped — unchanged behavior.
        var mapper = Build(new ControllerBinding(
            MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.DeckCue, ActionInputMode.Momentary));

        mapper.Apply(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        mapper.Apply(new MidiMessage(MidiMessageType.NoteOff, 0, 36, 0));

        PerformanceAction action = Assert.Single(_dispatcher.Dispatched);
        Assert.True(action.IsPressed);
    }

    [Fact]
    public void Apply_ReportReleaseBinding_DispatchesBothEdges_WithIsPressedFlag()
    {
        // An opt-in press-and-hold binding fires on press (IsPressed=true) AND release (false).
        var mapper = Build(new ControllerBinding(
            MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.DeckCue, ActionInputMode.Momentary,
            ReportRelease: true));

        mapper.Apply(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        mapper.Apply(new MidiMessage(MidiMessageType.NoteOff, 0, 36, 0));

        Assert.Equal(2, _dispatcher.Dispatched.Count);
        Assert.True(_dispatcher.Dispatched[0].IsPressed);
        Assert.False(_dispatcher.Dispatched[1].IsPressed);
    }

    [Fact]
    public void Apply_ReportReleaseCcButton_FiresPressAndReleaseFromData2()
    {
        var mapper = Build(new ControllerBinding(
            MidiMessageType.ControlChange, 0, 20, PerformanceActionKind.DeckCue, ActionInputMode.Momentary,
            ReportRelease: true));

        mapper.Apply(new MidiMessage(MidiMessageType.ControlChange, 0, 20, 127)); // press
        mapper.Apply(new MidiMessage(MidiMessageType.ControlChange, 0, 20, 0));   // release

        Assert.Equal(2, _dispatcher.Dispatched.Count);
        Assert.True(_dispatcher.Dispatched[0].IsPressed);
        Assert.False(_dispatcher.Dispatched[1].IsPressed);
    }

    [Fact]
    public void Apply_DispatchThrows_IsCaughtAndLogged()
    {
        _dispatcher.ThrowOnDispatch = true;
        var mapper = Build(new ControllerBinding(
            MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualBlackout, ActionInputMode.Momentary));

        var exception = Record.Exception(() => mapper.Apply(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127)));

        Assert.Null(exception);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void SetProfile_SwapsActiveProfile()
    {
        var mapper = Build();
        var next = new ControllerMappingProfile("next", "device", new[]
        {
            new ControllerBinding(MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualBlackout, ActionInputMode.Momentary),
        });

        mapper.SetProfile(next);
        mapper.Apply(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));

        Assert.Same(next, mapper.ActiveProfile);
        Assert.Single(_dispatcher.Dispatched);
    }
}
