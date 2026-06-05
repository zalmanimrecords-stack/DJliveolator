using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Live;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live;

public sealed class LiveViewModelVisualsTests
{
    public LiveViewModelVisualsTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class FakeVisualStage : IVisualStage
    {
        public int ShowCount { get; private set; }
        public bool IsShown { get; private set; }
        public void Show()
        {
            ShowCount++;
            IsShown = true;
        }
    }

    [Fact]
    public void CanShowVisuals_IsFalse_WhenNoStageWired()
    {
        var vm = new LiveViewModel();
        Assert.False(vm.CanShowVisuals);
    }

    [Fact]
    public async Task ShowVisualsCommand_LaunchesTheStage()
    {
        var stage = new FakeVisualStage();
        var vm = new LiveViewModel(visualStage: stage);

        Assert.True(vm.CanShowVisuals);
        await vm.ShowVisualsCommand.Execute().ToTask();

        Assert.Equal(1, stage.ShowCount);
        Assert.True(stage.IsShown);
    }
}
