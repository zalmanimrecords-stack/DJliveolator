using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Core.Tests.Mapping;

/// <summary>
/// The pipeline is the pure composition seam the App's composition root uses to turn an opened MIDI
/// device into a live driver of the dispatcher: it auto-selects a profile by device name, routes
/// input through the mapper, and (when a feedback output is present) publishes LED feedback. All of
/// it unit-tests with the existing MIDI fakes — no native library, no hardware.
/// </summary>
public class MidiInputPipelineTests
{
    private static readonly ControllerBinding PadBinding = new(
        MidiMessageType.NoteOn, 0, 36, PerformanceActionKind.VisualBlackout, ActionInputMode.Momentary);

    private static readonly ControllerMappingProfile MatchingProfile =
        new("Fake", "Fake Controller", new[] { PadBinding });

    private static MidiInputPipeline Build(
        FakeMidiInput input,
        IPerformanceActionDispatcher dispatcher,
        FakeMidiOutput? output = null,
        IEnumerable<ControllerMappingProfile>? profiles = null)
        => MidiInputPipeline.Create(
            input,
            output,
            dispatcher,
            profiles ?? new[] { MatchingProfile },
            NullLoggerFactory.Instance);

    [Fact]
    public void Create_OpensTheInput_AndRoutesMappedMessagesToTheDispatcher()
    {
        var input = new FakeMidiInput();
        var dispatcher = new RecordingDispatcher();

        using MidiInputPipeline pipeline = Build(input, dispatcher);

        Assert.True(input.IsOpen);
        input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualBlackout, action.Kind);
    }

    [Fact]
    public void Create_AutoSelectsProfileByDeviceName()
    {
        var input = new FakeMidiInput("Fake Controller");
        var dispatcher = new RecordingDispatcher();
        var other = ControllerMappingProfile.Empty("Other", "Some Other Device");

        using MidiInputPipeline pipeline = Build(input, dispatcher, profiles: new[] { other, MatchingProfile });

        Assert.Same(MatchingProfile, pipeline.ActiveProfile);
    }

    [Fact]
    public void Create_FallsBackToEmptyProfile_WhenNoHintMatches_StillRoutesNothing()
    {
        var input = new FakeMidiInput("Unknown Device");
        var dispatcher = new RecordingDispatcher();
        var nonMatching = ControllerMappingProfile.Empty("Other", "Some Other Device");

        using MidiInputPipeline pipeline = Build(input, dispatcher, profiles: new[] { nonMatching });

        // No profile matched, so the pipeline arms with an empty profile rather than mis-mapping —
        // input still flows (learn mode can capture), but nothing is dispatched yet.
        Assert.True(input.IsOpen);
        input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public void Create_WiresFeedbackOutput_WhenPresent()
    {
        var input = new FakeMidiInput();
        var output = new FakeMidiOutput();
        var dispatcher = new RecordingDispatcher();

        using MidiInputPipeline pipeline = Build(input, dispatcher, output);

        // A feedback change for the bound action/slot lights the control on the output.
        dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.VisualBlackout, Slot: 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0)));

        Assert.Contains(output.Sent, m => m.Data1 == 36 && m.Data2 == 127);
    }

    [Fact]
    public void Create_WithoutOutput_StillRoutesInput()
    {
        var input = new FakeMidiInput();
        var dispatcher = new RecordingDispatcher();

        using MidiInputPipeline pipeline = Build(input, dispatcher, output: null);

        input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        Assert.Single(dispatcher.Dispatched);
    }

    [Fact]
    public void Dispose_DetachesRoutingAndClosesTheInput()
    {
        var input = new FakeMidiInput();
        var dispatcher = new RecordingDispatcher();
        MidiInputPipeline pipeline = Build(input, dispatcher);

        pipeline.Dispose();

        Assert.False(input.IsOpen);
        Assert.True(input.Disposed);
        input.Emit(new MidiMessage(MidiMessageType.NoteOn, 0, 36, 127));
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public void Dispose_StopsPublishingFeedback()
    {
        var input = new FakeMidiInput();
        var output = new FakeMidiOutput();
        var dispatcher = new RecordingDispatcher();
        MidiInputPipeline pipeline = Build(input, dispatcher, output);

        pipeline.Dispose();
        dispatcher.RaiseFeedback(new ActionFeedbackChanged(
            PerformanceActionKind.VisualBlackout, Slot: 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0)));

        Assert.Empty(output.Sent);
    }

    [Fact]
    public void LearnSession_IsExposed_SoTheUiCanRemapBindings()
    {
        var input = new FakeMidiInput();
        var dispatcher = new RecordingDispatcher();

        using MidiInputPipeline pipeline = Build(input, dispatcher);

        Assert.NotNull(pipeline.LearnSession);
        Assert.False(pipeline.LearnSession.IsArmed);
    }
}
