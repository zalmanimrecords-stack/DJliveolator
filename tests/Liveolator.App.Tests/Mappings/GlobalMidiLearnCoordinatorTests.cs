using System.Reactive.Concurrency;
using Liveolator.App.Features.Mappings;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Settings;
using ReactiveUI;

namespace Liveolator.App.Tests.Mappings;

public sealed class GlobalMidiLearnCoordinatorTests
{
    public GlobalMidiLearnCoordinatorTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public void Enabled_UiActionBecomesLearnTargetWithoutDispatching()
    {
        var session = new FakeMidiControlSession();
        var inner = new RecordingDispatcher();
        using var coordinator = new GlobalMidiLearnCoordinator(session);
        var dispatcher = new LearningPerformanceActionDispatcher(inner, coordinator);
        coordinator.Enable();

        dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.DeckHotCue, Slot: 1, Argument: "4"));

        Assert.Empty(inner.Actions);
        Assert.Equal(PerformanceActionKind.DeckHotCue, session.LearnedAction);
        Assert.Equal(1, session.LearnedSlot);
        Assert.Equal("4", session.LearnedArgument);
        Assert.Equal(ActionInputMode.Momentary, session.LearnedInputMode);
        Assert.True(coordinator.IsWaitingForMidi);
    }

    [Fact]
    public void WaitingForMidi_SuppressesFurtherUiActions()
    {
        var session = new FakeMidiControlSession();
        var inner = new RecordingDispatcher();
        using var coordinator = new GlobalMidiLearnCoordinator(session);
        var dispatcher = new LearningPerformanceActionDispatcher(inner, coordinator);
        coordinator.Enable();
        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause));

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.VisualBlackout));

        Assert.Empty(inner.Actions);
        Assert.Equal(PerformanceActionKind.DeckPlayPause, session.LearnedAction);
    }

    [Fact]
    public void MappingCaptured_ReturnsToUiSelectionUntilEscapeCancels()
    {
        var session = new FakeMidiControlSession();
        using var coordinator = new GlobalMidiLearnCoordinator(session);
        coordinator.Enable();
        coordinator.TryCaptureUiAction(new PerformanceAction(PerformanceActionKind.DeckCue));

        session.RaiseMappingChanged();

        Assert.True(coordinator.IsEnabled);
        Assert.False(coordinator.IsWaitingForMidi);

        coordinator.Cancel();

        Assert.False(coordinator.IsEnabled);
        Assert.True(session.CancelCalled);
    }

    [Fact]
    public void Disabled_UiActionPassesThrough()
    {
        var session = new FakeMidiControlSession();
        var inner = new RecordingDispatcher();
        using var coordinator = new GlobalMidiLearnCoordinator(session);
        var dispatcher = new LearningPerformanceActionDispatcher(inner, coordinator);

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.VisualBlackout));

        Assert.Single(inner.Actions);
    }

    private sealed class FakeMidiControlSession : IMidiControlSession
    {
        public PerformanceActionKind? LearnedAction { get; private set; }
        public int LearnedSlot { get; private set; }
        public string? LearnedArgument { get; private set; }
        public ActionInputMode? LearnedInputMode { get; private set; }
        public RelativeEncoding LearnedEncoding { get; private set; }
        public bool CancelCalled { get; private set; }
        public ControllerMappingProfile? ActiveProfile => null;
        public bool IsLearnArmed { get; private set; }
        public bool IsInputConnected => true;
        public string? InputDeviceName => "CMD Studio 2a";
        public bool IsOutputConnected => false;
        public string? OutputDeviceName => null;
        public event EventHandler? ActivityDetected { add { } remove { } }
        public event EventHandler<ControllerMappingProfile>? MappingChanged;

        public Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Stop() { }

        public void BeginLearn(
            PerformanceActionKind action,
            int slot = 0,
            string? argument = null,
            ActionInputMode? preferredInputMode = null,
            double relativeTicksPerRevolution = 1.0,
            bool invert = false,
            RelativeEncoding relativeEncoding = RelativeEncoding.TwosComplement)
        {
            LearnedAction = action;
            LearnedSlot = slot;
            LearnedArgument = argument;
            LearnedInputMode = preferredInputMode;
            LearnedEncoding = relativeEncoding;
            IsLearnArmed = true;
        }

        public void CancelLearn()
        {
            CancelCalled = true;
            IsLearnArmed = false;
        }

        public Task RemoveBindingAsync(
            ControllerBinding binding,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void RaiseMappingChanged()
            => MappingChanged?.Invoke(
                this,
                ControllerMappingProfile.Empty("CMD Studio 2a", "CMD Studio 2a"));
    }

    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        public List<PerformanceAction> Actions { get; } = new();
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
        public void Dispatch(PerformanceAction action) => Actions.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => ActionFeedbackState.Unavailable;
    }
}
