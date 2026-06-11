using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Liveolator.Core.Tests.Actions;

public class PerformanceActionDispatcherTests
{
    private readonly CapturingLogger<PerformanceActionDispatcher> _logger = new();

    private PerformanceActionDispatcher Build(
        IEnumerable<IPerformanceActionHandler> handlers, IActionFeedbackSynchronizer? sync = null)
        => new(handlers, _logger, sync);

    [Fact]
    public void Dispatch_RoutesActionToOwningHandler()
    {
        var transport = new FakeActionHandler(PerformanceActionKind.TransportStop);
        var visual = new FakeActionHandler(PerformanceActionKind.VisualBlackout);
        using var dispatcher = Build(new[] { transport, visual });
        var action = new PerformanceAction(PerformanceActionKind.VisualBlackout);

        dispatcher.Dispatch(action);

        Assert.Empty(transport.Handled);
        Assert.Single(visual.Handled);
        Assert.Same(action, visual.Handled[0]);
    }

    [Fact]
    public void Dispatch_UnhandledKind_DoesNotThrow_AndLogsWarning()
    {
        var transport = new FakeActionHandler(PerformanceActionKind.TransportStop);
        using var dispatcher = Build(new[] { transport });

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.VisualBlackout));

        Assert.Empty(transport.Handled);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Dispatch_HandlerThrows_IsCaughtAndLoggedAsError()
    {
        var handler = new FakeActionHandler(PerformanceActionKind.BeatTapTempo) { ThrowOnHandle = true };
        using var dispatcher = Build(new[] { handler });

        var exception = Record.Exception(
            () => dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.BeatTapTempo)));

        Assert.Null(exception);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public void Dispatch_RaisesActionDispatched_ForEveryAction_IncludingUnrouted()
    {
        var transport = new FakeActionHandler(PerformanceActionKind.TransportStop);
        using var dispatcher = Build(new[] { transport });
        var seen = new List<PerformanceAction>();
        dispatcher.ActionDispatched += (_, a) => seen.Add(a);

        var routed = new PerformanceAction(PerformanceActionKind.TransportStop, Origin: "automix");
        var unrouted = new PerformanceAction(PerformanceActionKind.VisualBlackout);
        dispatcher.Dispatch(routed);
        dispatcher.Dispatch(unrouted);

        Assert.Equal(new[] { routed, unrouted }, seen);
        Assert.Equal("automix", seen[0].Origin);
    }

    [Fact]
    public void Dispatch_ThrowingActionObserver_DoesNotDropTheAction()
    {
        var handler = new FakeActionHandler(PerformanceActionKind.BeatTapTempo);
        using var dispatcher = Build(new[] { handler });
        dispatcher.ActionDispatched += (_, _) => throw new InvalidOperationException("observer boom");

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.BeatTapTempo));

        Assert.Single(handler.Handled);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void GetFeedback_RoutesToOwningHandler()
    {
        var handler = new FakeActionHandler(PerformanceActionKind.BeatLock)
        {
            FeedbackToReturn = new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0.5),
        };
        using var dispatcher = Build(new[] { handler });

        ActionFeedbackState feedback = dispatcher.GetFeedback(PerformanceActionKind.BeatLock);

        Assert.True(feedback.IsActive);
        Assert.Equal(0.5, feedback.Value);
    }

    [Fact]
    public void GetFeedback_UnhandledKind_ReturnsUnavailable()
    {
        using var dispatcher = Build(Array.Empty<IPerformanceActionHandler>());

        ActionFeedbackState feedback = dispatcher.GetFeedback(PerformanceActionKind.BeatLock);

        Assert.Equal(ActionFeedbackState.Unavailable, feedback);
    }

    [Fact]
    public void GetFeedback_HandlerThrows_ReturnsUnavailable_AndLogsError()
    {
        var handler = new FakeActionHandler(PerformanceActionKind.BeatLock) { ThrowOnGetFeedback = true };
        using var dispatcher = Build(new[] { handler });

        ActionFeedbackState feedback = dispatcher.GetFeedback(PerformanceActionKind.BeatLock);

        Assert.Equal(ActionFeedbackState.Unavailable, feedback);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void HandlerFeedback_IsReRaisedByDispatcher()
    {
        var handler = new FakeActionHandler(PerformanceActionKind.BeatLock);
        using var dispatcher = Build(new[] { handler });
        ActionFeedbackChanged? received = null;
        dispatcher.FeedbackChanged += (_, e) => received = e;
        var state = new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 1);

        handler.Raise(PerformanceActionKind.BeatLock, slot: 2, state);

        Assert.NotNull(received);
        Assert.Equal(PerformanceActionKind.BeatLock, received!.Kind);
        Assert.Equal(2, received.Slot);
        Assert.Equal(state, received.State);
    }

    [Fact]
    public void FeedbackNotifications_GoThroughTheSynchronizer()
    {
        var handler = new FakeActionHandler(PerformanceActionKind.BeatLock);
        var sync = new RecordingSynchronizer();
        using var dispatcher = Build(new[] { handler }, sync);
        dispatcher.FeedbackChanged += (_, _) => { };

        handler.Raise(PerformanceActionKind.BeatLock, slot: 0, ActionFeedbackState.Unavailable);

        Assert.Equal(1, sync.PostCount);
    }

    [Fact]
    public void Constructor_RejectsKindClaimedByTwoHandlers()
    {
        var a = new FakeActionHandler(PerformanceActionKind.BeatLock);
        var b = new FakeActionHandler(PerformanceActionKind.BeatLock);

        Assert.Throws<ArgumentException>(() => Build(new[] { a, b }));
    }

    [Fact]
    public void Constructor_StrictOwnership_RejectsMissingKinds()
    {
        var handler = new FakeActionHandler(PerformanceActionKind.BeatLock);

        Assert.Throws<ArgumentException>(() =>
            new PerformanceActionDispatcher(new[] { handler }, _logger, requireCompleteOwnership: true));
    }

    [Fact]
    public void Constructor_StrictOwnership_AcceptsEveryKindExactlyOnce()
    {
        var handler = new FakeActionHandler(Enum.GetValues<PerformanceActionKind>());

        using var dispatcher =
            new PerformanceActionDispatcher(new[] { handler }, _logger, requireCompleteOwnership: true);
    }

    [Fact]
    public void Dispose_StopsReRaisingHandlerFeedback()
    {
        var handler = new FakeActionHandler(PerformanceActionKind.BeatLock);
        var dispatcher = Build(new[] { handler });
        int count = 0;
        dispatcher.FeedbackChanged += (_, _) => count++;

        dispatcher.Dispose();
        handler.Raise(PerformanceActionKind.BeatLock, slot: 0, ActionFeedbackState.Unavailable);

        Assert.Equal(0, count);
    }

    /// <summary>Synchronizer that counts posts and runs them inline, to prove the dispatcher uses it.</summary>
    private sealed class RecordingSynchronizer : IActionFeedbackSynchronizer
    {
        public int PostCount { get; private set; }

        public void Post(Action work)
        {
            PostCount++;
            work();
        }
    }
}
