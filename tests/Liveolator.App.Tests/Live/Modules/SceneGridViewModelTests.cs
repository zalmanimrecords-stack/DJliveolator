using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class SceneGridViewModelTests
{
    public SceneGridViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public void Builds_64_Pads()
    {
        var vm = new SceneGridViewModel(new FakeDispatcher());
        Assert.Equal(64, vm.Pads.Count);
    }

    [Fact]
    public async Task Pad_EmitsVisualLoadScene_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new SceneGridViewModel(dispatcher);

        await vm.Pads[5].LaunchCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualLoadScene, action.Kind);
        Assert.Equal(5, action.Slot);
    }

    [Fact]
    public void SelectingBank_EmitsVisualSelectBank()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new SceneGridViewModel(dispatcher);

        vm.SelectedBankIndex = 2;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.VisualSelectBank, action.Kind);
        Assert.Equal(2, action.Slot);
    }

    [Fact]
    public void Pads_SeedLoadedStateFromFeedback()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(PerformanceActionKind.VisualLoadScene, 3,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0));

        var vm = new SceneGridViewModel(dispatcher);

        Assert.True(vm.Pads[3].IsLoaded);
        Assert.False(vm.Pads[0].IsLoaded);
    }

    [Fact]
    public void Feedback_LightsActivePad()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new SceneGridViewModel(dispatcher);

        dispatcher.RaiseFeedback(PerformanceActionKind.VisualLoadScene, 3,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        Assert.True(vm.Pads[3].IsActive);
        Assert.True(vm.Pads[3].IsLoaded);
    }
}
