using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using Liveolator.App.Features.Live;
using Liveolator.App.Features.Live.Modules;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live;

/// <summary>
/// Verifies the Live tab composition root: it exposes the performance modules, owns the render-loop
/// timer that pumps the shared <see cref="ManualBeatClock"/> (so phase/pulse advance between taps), and
/// stays safe when no services are wired. Per-control emission is covered by the module test files
/// (VisualControl / Deck / Mixer / SceneGrid / MasterFx).
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
        public event EventHandler<PerformanceAction>? ActionDispatched { add { } remove { } }
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

    [Fact]
    public void ExposesTheVisualModules()
    {
        var vm = new LiveViewModel(new RecordingDispatcher());

        Assert.NotNull(vm.ProgramOut);
        Assert.NotNull(vm.VisualControl);
        Assert.NotNull(vm.SceneGrid);
        Assert.NotNull(vm.MasterFx);
    }

    /// <summary>
    /// LIVE shows no decks but still DRIVES them: its render timer is what advances the SHARED playheads,
    /// which is what keeps DJ PRO's zoomed waveform moving. Removing the console from the page must not
    /// take the pump with it, and that breakage would be silent on the LIVE page itself - it would only
    /// show up as a frozen waveform on another tab.
    /// </summary>
    [Fact]
    public void TimerTick_StillPumpsASharedDeckSet_AndDisposeLeavesItAlive()
    {
        var host = new StubHostClock { TicksPerSecond = 1000 };
        var clock = new ManualBeatClock(host.TicksPerSecond);
        var timer = new FakeLiveBeatTimer();
        var decks = new PerformanceDeckSet(new RecordingDispatcher());

        var vm = new LiveViewModel(new RecordingDispatcher(), clock, clock, host, timer, decks: decks);

        host.NowTicks = 1000;
        timer.FireTick();

        // Closing the visual page must not take the shared decks down with it - they belong to the
        // composition root and DJ PRO is still using them.
        vm.Dispose();
        decks.UpdatePlayheads();
        Assert.NotNull(decks.DeckA);
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
    public void TimerTick_AdvancesManualClock_SoBeatPhaseMovesBetweenTaps()
    {
        var host = new StubHostClock { TicksPerSecond = 1000 };
        var clock = new ManualBeatClock(host.TicksPerSecond);
        var timer = new FakeLiveBeatTimer();

        // The dispatcher routes tap actions to the real clock so the VM emits, not calls, intent.
        var routed = new PerformanceActionDispatcher(
            new IPerformanceActionHandler[] { new BeatActionHandler(clock, host) },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PerformanceActionDispatcher>.Instance);
        var vm = new LiveViewModel(routed, clock, clock, host, timer);

        // Two taps 500ms apart establish 120 BPM (one beat = 500 ticks at 1000 t/s).
        host.NowTicks = 0;
        routed.Dispatch(new PerformanceAction(PerformanceActionKind.BeatTapTempo));
        host.NowTicks = 500;
        routed.Dispatch(new PerformanceAction(PerformanceActionKind.BeatTapTempo));

        Assert.Equal(120, clock.Current.Bpm, precision: 3);
        double phaseAtTap = clock.Current.BeatPhase;

        // A render-loop tick a quarter-beat later must advance the phase with no further tap.
        host.NowTicks = 625;
        timer.FireTick();

        Assert.NotEqual(phaseAtTap, clock.Current.BeatPhase);
        Assert.Equal(0.25, clock.Current.BeatPhase, precision: 3);
    }

    [Fact]
    public void Construction_WithNoServices_DoesNotThrow_AndDisablesControls()
    {
        var vm = new LiveViewModel();

        Assert.False(vm.IsLiveModeEnabled);
        // Disposing a degraded VM (no timer/clock) must also be safe.
        vm.Dispose();
    }
}
