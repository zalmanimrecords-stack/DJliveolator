using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class MasterFxViewModelTests
{
    public MasterFxViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public async Task StrobeCommand_EmitsVisualToggleStrobe()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MasterFxViewModel(dispatcher);

        await vm.StrobeCommand.Execute().ToTask();

        Assert.Equal(PerformanceActionKind.VisualToggleStrobe, Assert.Single(dispatcher.Dispatched).Kind);
    }

    [Fact]
    public async Task BlackoutCommand_EmitsVisualBlackout()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MasterFxViewModel(dispatcher);

        await vm.BlackoutCommand.Execute().ToTask();

        Assert.Equal(PerformanceActionKind.VisualBlackout, Assert.Single(dispatcher.Dispatched).Kind);
    }

    [Fact]
    public async Task RecordCommand_EmitsMasterRecordToggle()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MasterFxViewModel(dispatcher);

        await vm.RecordCommand.Execute().ToTask();

        Assert.Equal(PerformanceActionKind.MasterRecordToggle, Assert.Single(dispatcher.Dispatched).Kind);
    }

    [Fact]
    public void Feedback_LatchesRecordingStateAndAvailability()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MasterFxViewModel(dispatcher);

        dispatcher.RaiseFeedback(PerformanceActionKind.MasterRecordToggle, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        Assert.True(vm.IsRecording);
        Assert.True(vm.IsRecordEnabled);
    }

    [Fact]
    public void RecordEnabled_SeededFromInitialFeedback()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(PerformanceActionKind.MasterRecordToggle, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0));

        var vm = new MasterFxViewModel(dispatcher);

        Assert.True(vm.IsRecordEnabled);
        Assert.False(vm.IsRecording);
    }

    [Fact]
    public void RecordDisabled_WhenRecorderUnavailable()
    {
        // FakeDispatcher returns Unavailable feedback by default (no realtime engine).
        var vm = new MasterFxViewModel(new FakeDispatcher());

        Assert.False(vm.IsRecordEnabled);
    }

    [Fact]
    public void Feedback_LatchesStrobeAndBlackoutState()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MasterFxViewModel(dispatcher);

        dispatcher.RaiseFeedback(PerformanceActionKind.VisualToggleStrobe, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        dispatcher.RaiseFeedback(PerformanceActionKind.VisualBlackout, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        Assert.True(vm.IsStrobe);
        Assert.True(vm.IsBlackout);
    }

    [Fact]
    public void MasterAndSwing_AreDisabled_NoBackendYet()
    {
        var vm = new MasterFxViewModel(new FakeDispatcher());

        Assert.False(vm.Master.IsEnabled);
        Assert.False(vm.Swing.IsEnabled);
    }
}
