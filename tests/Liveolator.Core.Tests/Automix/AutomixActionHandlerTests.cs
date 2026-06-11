using Liveolator.Core.Actions;
using Liveolator.Core.Automix;
using Liveolator.Core.Beat;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Core.Tests.Automix;

public class AutomixActionHandlerTests
{
    private readonly AutomixController _controller;
    private readonly AutomixActionHandler _handler;

    public AutomixActionHandlerTests()
    {
        _controller = new AutomixController(new IdleClock(), new EmptyReader());
        _controller.Attach(new NullDispatcher());
        _handler = new AutomixActionHandler(_controller);
    }

    [Fact]
    public void OwnsExactlyTheAutomixKinds()
    {
        Assert.Equal(
            new HashSet<PerformanceActionKind>
            {
                PerformanceActionKind.AutomixToggle,
                PerformanceActionKind.AutomixSetDuration,
                PerformanceActionKind.AutomixSetStyle,
            },
            _handler.HandledKinds);
    }

    [Fact]
    public void SetDuration_RoutesTheKnobAndReportsResolvedBars()
    {
        _handler.Handle(new PerformanceAction(
            PerformanceActionKind.AutomixSetDuration, ActionInputMode.Absolute, Value: 1.0));

        ActionFeedbackState feedback = _handler.GetFeedback(PerformanceActionKind.AutomixSetDuration, 0);
        Assert.Equal(1.0, feedback.Value, precision: 9);
        Assert.Equal("64", feedback.Argument);
    }

    [Fact]
    public void SetStyle_ParsesTheArgumentCaseInsensitively()
    {
        _handler.Handle(new PerformanceAction(
            PerformanceActionKind.AutomixSetStyle, Argument: "eqmix"));

        Assert.Equal(AutomixStyle.EqMix, _controller.Style);
        Assert.Equal("EqMix", _handler.GetFeedback(PerformanceActionKind.AutomixSetStyle, 0).Argument);
    }

    [Fact]
    public void SetStyle_InvalidArgument_ThrowsInsteadOfGuessing()
        => Assert.Throws<ArgumentException>(() => _handler.Handle(new PerformanceAction(
            PerformanceActionKind.AutomixSetStyle, Argument: "wobble")));

    [Fact]
    public void Toggle_RefusedStart_SurfacesTheReasonInFeedback()
    {
        _handler.Handle(new PerformanceAction(PerformanceActionKind.AutomixToggle));

        ActionFeedbackState feedback = _handler.GetFeedback(PerformanceActionKind.AutomixToggle, 0);
        Assert.False(feedback.IsActive);
        Assert.True(feedback.IsAvailable);
        Assert.Equal(nameof(AutomixRefusal.NothingPlaying), feedback.Argument);
    }

    [Fact]
    public void ControllerChanges_PublishFeedback()
    {
        var seen = new List<PerformanceActionKind>();
        _handler.FeedbackChanged += (_, e) => seen.Add(e.Kind);

        _controller.SetDurationKnob(0.4);

        Assert.Contains(PerformanceActionKind.AutomixSetDuration, seen);
        Assert.Contains(PerformanceActionKind.AutomixToggle, seen);
    }

    private sealed class IdleClock : IBeatClock
    {
        public BeatClockState Current => BeatClockState.Idle;

        public event EventHandler<BeatClockState>? StateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class NullDispatcher : IPerformanceActionDispatcher
    {
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<PerformanceAction>? ActionDispatched
        {
            add { }
            remove { }
        }

        public void Dispatch(PerformanceAction action)
        {
        }

        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => ActionFeedbackState.Unavailable;
    }

    private sealed class EmptyReader : IAutomixDeckReader
    {
        public AutomixDeckSnapshot ReadDeck(int slot) => new(
            IsLoaded: false, IsPlaying: false, BaseBpm: 0, EffectiveBpm: 0,
            FirstBeatSeconds: 0, PositionSeconds: 0, LengthSeconds: 0,
            SyncState: Liveolator.Core.Audio.Sync.SyncLockState.Off, SyncLocked: false);

        public MixerState Mixer => MixerState.Default;
    }
}
