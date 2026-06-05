using System;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class BeatEngineViewModelTests
{
    public BeatEngineViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
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

    [Theory]
    [InlineData(nameof(BeatEngineViewModel.TapCommand), PerformanceActionKind.BeatTapTempo)]
    [InlineData(nameof(BeatEngineViewModel.HalfCommand), PerformanceActionKind.BeatHalfTempo)]
    [InlineData(nameof(BeatEngineViewModel.DoubleCommand), PerformanceActionKind.BeatDoubleTempo)]
    [InlineData(nameof(BeatEngineViewModel.SetDownbeatCommand), PerformanceActionKind.BeatSetDownbeat)]
    [InlineData(nameof(BeatEngineViewModel.NudgeForwardCommand), PerformanceActionKind.BeatNudgeForward)]
    [InlineData(nameof(BeatEngineViewModel.NudgeBackwardCommand), PerformanceActionKind.BeatNudgeBackward)]
    [InlineData(nameof(BeatEngineViewModel.ResetCommand), PerformanceActionKind.BeatResetGrid)]
    public async Task Command_EmitsExpectedActionKind(string commandName, PerformanceActionKind expected)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new BeatEngineViewModel(dispatcher);

        var command = (ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit>)
            typeof(BeatEngineViewModel).GetProperty(commandName)!.GetValue(vm)!;
        await command.Execute().ToTask();

        Assert.Single(dispatcher.Dispatched);
        Assert.Equal(expected, dispatcher.Dispatched[0].Kind);
    }

    [Fact]
    public async Task LockToggle_EmitsLock_WhenUnlocked_AndUnlock_WhenLocked()
    {
        var dispatcher = new FakeDispatcher();
        var clock = new FakeBeatClock();
        var vm = new BeatEngineViewModel(dispatcher, clock);

        await vm.LockToggleCommand.Execute().ToTask();
        Assert.Equal(PerformanceActionKind.BeatLock, dispatcher.Dispatched[^1].Kind);

        clock.Publish(new BeatClockState(
            Bpm: 128, Confidence: 0.9, BeatPhase: 0, BarPhase: 0, BeatCount: 0, BarNumber: 0,
            IsBeat: false, IsDownbeat: false, IsLocked: true, Source: BeatClockSource.Manual,
            Candidates: Array.Empty<TempoCandidate>()));

        await vm.LockToggleCommand.Execute().ToTask();
        Assert.Equal(PerformanceActionKind.BeatUnlock, dispatcher.Dispatched[^1].Kind);
    }

    [Fact]
    public void Readout_UpdatesFromBeatClockState()
    {
        var clock = new FakeBeatClock();
        var vm = new BeatEngineViewModel(new FakeDispatcher(), clock);

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
    public void Auto_IsDisabled_NoBackendYet()
    {
        var vm = new BeatEngineViewModel(new FakeDispatcher());
        Assert.False(vm.IsAutoEnabled);
        Assert.True(vm.IsEnabled);
    }

    [Fact]
    public void NoDispatcher_DisablesModule()
    {
        var vm = new BeatEngineViewModel();
        Assert.False(vm.IsEnabled);
    }
}
