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
