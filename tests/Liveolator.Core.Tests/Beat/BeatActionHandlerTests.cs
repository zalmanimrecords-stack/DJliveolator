using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Tests.Actions;
using Xunit;

namespace Liveolator.Core.Tests.Beat;

public class BeatActionHandlerTests
{
    private const long Ms = 1000;

    private readonly ManualBeatClock _clock = new(Ms);
    private readonly FakeHostClock _host = new(Ms);
    private readonly BeatActionHandler _handler;

    public BeatActionHandlerTests() => _handler = new BeatActionHandler(_clock, _host);

    private void Handle(PerformanceActionKind kind) => _handler.Handle(new PerformanceAction(kind));

    private void TapAt(long now)
    {
        _host.NowTicks = now;
        Handle(PerformanceActionKind.BeatTapTempo);
    }

    [Fact]
    public void HandledKinds_CoverTheNineBeatActions()
    {
        Assert.Equal(9, _handler.HandledKinds.Count);
        Assert.Contains(PerformanceActionKind.BeatTapTempo, _handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.BeatSetDownbeat, _handler.HandledKinds);
    }

    [Fact]
    public void TapTempo_DrivesTheClock()
    {
        TapAt(0);
        TapAt(500);

        Assert.Equal(120, _clock.Bpm, precision: 6);
    }

    [Fact]
    public void Lock_LocksClock_AndRaisesFeedback()
    {
        ActionFeedbackChanged? feedback = null;
        _handler.FeedbackChanged += (_, e) => feedback = e;

        Handle(PerformanceActionKind.BeatLock);

        Assert.True(_clock.IsLocked);
        Assert.NotNull(feedback);
        Assert.Equal(PerformanceActionKind.BeatLock, feedback!.Kind);
        Assert.True(feedback.State.IsActive);
    }

    [Fact]
    public void Unlock_UnlocksClock()
    {
        Handle(PerformanceActionKind.BeatLock);
        Handle(PerformanceActionKind.BeatUnlock);

        Assert.False(_clock.IsLocked);
    }

    [Fact]
    public void GetFeedback_BeatLock_ReflectsClockState()
    {
        Handle(PerformanceActionKind.BeatLock);

        ActionFeedbackState feedback = _handler.GetFeedback(PerformanceActionKind.BeatLock, slot: 0);

        Assert.True(feedback.IsActive);
    }

    [Fact]
    public void HalfAndDoubleTempo_AdjustClock()
    {
        TapAt(0);
        TapAt(500); // 120 BPM
        _host.NowTicks = 500;

        Handle(PerformanceActionKind.BeatHalfTempo);
        Assert.Equal(60, _clock.Bpm, precision: 6);

        Handle(PerformanceActionKind.BeatDoubleTempo);
        Assert.Equal(120, _clock.Bpm, precision: 6);
    }

    [Fact]
    public void NudgeForward_ShiftsPhaseByConfiguredAmount()
    {
        TapAt(0);
        TapAt(500); // grid anchored at 500
        _host.NowTicks = 500;
        _clock.Update(500); // phase 0 at the anchor

        Handle(PerformanceActionKind.BeatNudgeForward);

        Assert.Equal(BeatActionHandler.DefaultNudgeBeats, _clock.Current.BeatPhase, precision: 6);
    }

    [Fact]
    public void RelativeNudge_HonorsSignedEncoderMagnitude()
    {
        TapAt(0);
        TapAt(500);
        _host.NowTicks = 500;
        _clock.Update(500);

        _handler.Handle(new PerformanceAction(
            PerformanceActionKind.BeatNudgeForward,
            ActionInputMode.Relative,
            Value: -3));

        Assert.Equal(1.0 - (3 * BeatActionHandler.DefaultNudgeBeats),
            _clock.Current.BeatPhase, precision: 6);
    }

    [Fact]
    public void ResetGrid_ReanchorsToNow()
    {
        TapAt(0);
        TapAt(500);
        _host.NowTicks = 1777;
        _clock.Update(1777);

        Handle(PerformanceActionKind.BeatResetGrid);

        Assert.Equal(0, _clock.Current.BeatCount);
        Assert.Equal(0.0, _clock.Current.BeatPhase, precision: 6);
    }

    [Fact]
    public void EndToEnd_DispatcherRoutesBeatLockToTheClock()
    {
        using var dispatcher = new PerformanceActionDispatcher(
            new[] { _handler }, new CapturingLogger<PerformanceActionDispatcher>());
        ActionFeedbackChanged? feedback = null;
        dispatcher.FeedbackChanged += (_, e) => feedback = e;

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.BeatLock));

        Assert.True(_clock.IsLocked);
        Assert.NotNull(feedback); // feedback propagated through the dispatcher
        Assert.True(dispatcher.GetFeedback(PerformanceActionKind.BeatLock).IsActive);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new BeatActionHandler(null!, _host));
        Assert.Throws<ArgumentNullException>(() => new BeatActionHandler(_clock, null!));
    }
}
