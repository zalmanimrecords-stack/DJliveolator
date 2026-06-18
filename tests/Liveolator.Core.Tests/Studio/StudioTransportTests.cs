using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class StudioTransportTests
{
    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        public List<PerformanceAction> Dispatched { get; } = new();
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0) => ActionFeedbackState.Unavailable;
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged { add { } remove { } }
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
    }

    private static StudioTransport Build(RecordingDispatcher dispatcher, StudioProject project)
        => new(new StudioArranger(project), dispatcher, new SystemHostClock());

    private static StudioProject OneClipWithGain() => new("p", 120,
        new[] { new StudioClip(2, "/m/x.wav", TimelineStartSeconds: 8, TimeSpan.Zero, TimeSpan.FromSeconds(10)) },
        new[]
        {
            new AutomationLane(AutomationTarget.DeckGain, 2, new[]
            {
                new AutomationKeyframe(8, 0.0),
                new AutomationKeyframe(18, 1.0),
            }),
        });

    [Fact]
    public void Advance_ClipStart_DispatchesLoadThenPlay_StampedStudio()
    {
        var dispatcher = new RecordingDispatcher();
        using StudioTransport transport = Build(dispatcher, OneClipWithGain());

        transport.Advance(9); // crosses the clip start at 8

        var transportActions = dispatcher.Dispatched
            .Where(a => a.Kind is PerformanceActionKind.DeckLoadTrack or PerformanceActionKind.DeckPlayPause)
            .ToList();
        Assert.Equal(2, transportActions.Count);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, transportActions[0].Kind);
        Assert.Equal("/m/x.wav", transportActions[0].Argument);
        Assert.Equal(2, transportActions[0].Slot);
        Assert.Equal(PerformanceActionKind.DeckPlayPause, transportActions[1].Kind);
        Assert.All(transportActions, a => Assert.Equal(StudioArranger.Origin, a.Origin));
    }

    [Fact]
    public void Advance_PastClipEnd_DispatchesTransportStopForThatDeck()
    {
        var dispatcher = new RecordingDispatcher();
        using StudioTransport transport = Build(dispatcher, OneClipWithGain());

        transport.Advance(9);   // start
        dispatcher.Dispatched.Clear();
        transport.Advance(19);  // clip ends at 18

        PerformanceAction stop = Assert.Single(
            dispatcher.Dispatched.Where(a => a.Kind == PerformanceActionKind.TransportStop));
        Assert.Equal(2, stop.Slot);
    }

    [Fact]
    public void Advance_EmitsAutomationValueAtPosition()
    {
        var dispatcher = new RecordingDispatcher();
        using StudioTransport transport = Build(dispatcher, OneClipWithGain());

        transport.Advance(13); // halfway through the 8..18 gain ramp → 0.5

        PerformanceAction gain = Assert.Single(
            dispatcher.Dispatched.Where(a => a.Kind == PerformanceActionKind.MixerChannelGain));
        Assert.Equal(2, gain.Slot);
        Assert.Equal(0.5, gain.Value, 1e-9);
    }

    [Fact]
    public void Advance_DoesNotRefireClipStartOnLaterAdvance()
    {
        var dispatcher = new RecordingDispatcher();
        using StudioTransport transport = Build(dispatcher, OneClipWithGain());

        transport.Advance(9);
        dispatcher.Dispatched.Clear();
        transport.Advance(10); // still inside the clip, past its start

        Assert.Empty(dispatcher.Dispatched.Where(a => a.Kind == PerformanceActionKind.DeckLoadTrack));
    }

    [Fact]
    public void Seek_ResetsDispatchWindow_SoEarlierClipsDoNotReplay()
    {
        var dispatcher = new RecordingDispatcher();
        using StudioTransport transport = Build(dispatcher, OneClipWithGain());

        transport.Seek(12); // past the clip start
        transport.Advance(13);

        Assert.Empty(dispatcher.Dispatched.Where(a => a.Kind == PerformanceActionKind.DeckLoadTrack));
        Assert.Equal(13, transport.PositionSeconds, 1e-9);
    }
}
