using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live;

/// <summary>
/// Verifies the Live tab view-model: every control emits the right <see cref="PerformanceAction"/>
/// through the dispatcher (never a direct engine call), the readout follows the beat clock, and the
/// render-loop timer pumps the manual clock so phase/pulse advance between taps. Logic is tested with
/// a fake dispatcher, a fake timer, and the real <see cref="ManualBeatClock"/>.
/// </summary>
public sealed class LiveViewModelTests
{
    public LiveViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        public List<PerformanceAction> Dispatched { get; } = new();
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0) => ActionFeedbackState.Unavailable;
        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged { add { } remove { } }
    }

    private sealed class FakeBeatClock : IBeatClock
    {
        public BeatClockState Current { get; private set; } = BeatClockState.Idle;
        public event EventHandler<BeatClockState>? StateChanged;
        public void Publish(BeatClockState state)
        {
            Current = state;
            StateChanged?.Invoke(this, state);
        }
    }

    private sealed class FakeLiveBeatTimer : ILiveBeatTimer
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public event EventHandler? Tick;
        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void FireTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class StubHostClock : IHostClock
    {
        public long TicksPerSecond { get; init; } = 1000;
        public long NowTicks { get; set; }
    }

    [Fact]
    public void LiveModeDisabled_WhenNoDispatcher()
    {
        var vm = new LiveViewModel();

        Assert.False(vm.IsLiveModeEnabled);
    }

    [Fact]
    public void LiveModeEnabled_WhenDispatcherProvided()
    {
        var vm = new LiveViewModel(new RecordingDispatcher());

        Assert.True(vm.IsLiveModeEnabled);
    }

    [Theory]
    [InlineData(nameof(LiveViewModel.TapCommand), PerformanceActionKind.BeatTapTempo)]
    [InlineData(nameof(LiveViewModel.LockCommand), PerformanceActionKind.BeatLock)]
    [InlineData(nameof(LiveViewModel.UnlockCommand), PerformanceActionKind.BeatUnlock)]
    [InlineData(nameof(LiveViewModel.HalfCommand), PerformanceActionKind.BeatHalfTempo)]
    [InlineData(nameof(LiveViewModel.DoubleCommand), PerformanceActionKind.BeatDoubleTempo)]
    [InlineData(nameof(LiveViewModel.NudgeForwardCommand), PerformanceActionKind.BeatNudgeForward)]
    [InlineData(nameof(LiveViewModel.NudgeBackwardCommand), PerformanceActionKind.BeatNudgeBackward)]
    [InlineData(nameof(LiveViewModel.SetDownbeatCommand), PerformanceActionKind.BeatSetDownbeat)]
    [InlineData(nameof(LiveViewModel.PlayPauseCommand), PerformanceActionKind.DeckPlayPause)]
    [InlineData(nameof(LiveViewModel.StopCommand), PerformanceActionKind.TransportStop)]
    public async Task Command_EmitsExpectedActionKind(string commandName, PerformanceActionKind expected)
    {
        var dispatcher = new RecordingDispatcher();
        var vm = new LiveViewModel(dispatcher);

        var command = (ReactiveUI.ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit>)
            typeof(LiveViewModel).GetProperty(commandName)!.GetValue(vm)!;
        await command.Execute().ToTask();

        Assert.Single(dispatcher.Dispatched);
        Assert.Equal(expected, dispatcher.Dispatched[0].Kind);
    }

    [Fact]
    public void Readout_UpdatesFromBeatClockState()
    {
        var clock = new FakeBeatClock();
        var vm = new LiveViewModel(new RecordingDispatcher(), clock);

        Assert.Equal("—", vm.Bpm);

        clock.Publish(new BeatClockState(
            Bpm: 128.0, Confidence: 0.9, BeatPhase: 0.5, BarPhase: 0.25,
            BeatCount: 7, BarNumber: 1, IsBeat: true, IsDownbeat: false,
            IsLocked: true, Source: BeatClockSource.Manual, Candidates: Array.Empty<TempoCandidate>()));

        Assert.Contains("128", vm.Bpm);
        Assert.Contains("90", vm.Confidence);
        Assert.True(vm.IsLocked);
        Assert.Equal(0.5, vm.BeatPhase);
        Assert.Equal(0.25, vm.BarPhase);
        Assert.Equal(7, vm.BeatCount);
        Assert.Equal(1, vm.BarNumber);
        Assert.True(vm.IsBeat);
        Assert.False(vm.IsDownbeat);
    }

    [Fact]
    public void Timer_IsStartedOnConstruction_AndStoppedOnDispose()
    {
        var timer = new FakeLiveBeatTimer();
        var clock = new ManualBeatClock(1000);

        var vm = new LiveViewModel(new RecordingDispatcher(), clock, clock, new StubHostClock(), timer);
        Assert.True(timer.Started);

        vm.Dispose();
        Assert.True(timer.Stopped);
    }

    [Fact]
    public async Task TimerTick_AdvancesManualClock_SoPhaseMovesBetweenTaps()
    {
        var host = new StubHostClock { TicksPerSecond = 1000 };
        var clock = new ManualBeatClock(host.TicksPerSecond);
        var timer = new FakeLiveBeatTimer();
        var dispatcher = new RecordingDispatcher();

        // The dispatcher routes tap actions to the real clock so the VM emits, not calls, intent.
        var routed = new PerformanceActionDispatcher(
            new IPerformanceActionHandler[] { new BeatActionHandler(clock, host) },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PerformanceActionDispatcher>.Instance);
        var vm = new LiveViewModel(routed, clock, clock, host, timer);

        // Two taps 500ms apart establish 120 BPM (one beat = 500 ticks at 1000 t/s).
        host.NowTicks = 0;
        await vm.TapCommand.Execute().ToTask();
        host.NowTicks = 500;
        await vm.TapCommand.Execute().ToTask();

        Assert.Contains("120", vm.Bpm);
        double phaseAtTap = vm.BeatPhase;

        // A render-loop tick a quarter-beat later must advance the phase with no further tap.
        host.NowTicks = 625;
        timer.FireTick();

        Assert.NotEqual(phaseAtTap, vm.BeatPhase);
        Assert.Equal(0.25, vm.BeatPhase, precision: 3);
    }

    [Fact]
    public void Construction_WithNoServices_DoesNotThrow_AndDisablesControls()
    {
        var vm = new LiveViewModel();

        Assert.False(vm.IsLiveModeEnabled);
        Assert.Equal("—", vm.Bpm);
        // Disposing a degraded VM (no timer/clock) must also be safe.
        vm.Dispose();
    }
}
