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
