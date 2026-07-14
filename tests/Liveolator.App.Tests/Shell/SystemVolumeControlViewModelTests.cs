using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Shell;

public class SystemVolumeControlViewModelTests
{
    public SystemVolumeControlViewModelTests()
    {
        // Run the feedback marshalling synchronously so RaiseFeedback is observable in-line.
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
    }

    private static FakeDispatcher AvailableDispatcher(double level = 0.5)
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(
            PerformanceActionKind.SystemMasterVolume, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: level));
        return dispatcher;
    }

    [Fact]
    public void AvailableHost_EnablesKnobAndSeedsCurrentLevel()
    {
        var vm = new SystemVolumeControlViewModel(AvailableDispatcher(level: 0.42));

        Assert.True(vm.IsAvailable);
        Assert.True(vm.Volume.IsEnabled);
        Assert.Equal(0.42, vm.Volume.Value, 1e-9);
    }

    [Fact]
    public void UserChange_EmitsAbsoluteSystemMasterVolumeAction()
    {
        FakeDispatcher dispatcher = AvailableDispatcher();
        var vm = new SystemVolumeControlViewModel(dispatcher);

        vm.Volume.Value = 0.8;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.SystemMasterVolume, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(0.8, action.Value, 1e-9);
    }

    [Fact]
    public void Feedback_UpdatesValueWithoutReEmitting()
    {
        FakeDispatcher dispatcher = AvailableDispatcher(level: 0.5);
        var vm = new SystemVolumeControlViewModel(dispatcher);

        dispatcher.RaiseFeedback(
            PerformanceActionKind.SystemMasterVolume, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.9));

        Assert.Equal(0.9, vm.Volume.Value, 1e-9);
        Assert.Empty(dispatcher.Dispatched); // feedback must not loop back into a dispatch
    }

    [Fact]
    public void UnavailableHost_DisablesKnobAndNeverEmits()
    {
        // No seeded feedback → GetFeedback returns Unavailable.
        var dispatcher = new FakeDispatcher();
        var vm = new SystemVolumeControlViewModel(dispatcher);

        Assert.False(vm.IsAvailable);
        Assert.False(vm.Volume.IsEnabled);

        vm.Volume.Value = 0.3;
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public void NullDispatcher_DisablesKnob()
    {
        var vm = new SystemVolumeControlViewModel(dispatcher: null);

        Assert.False(vm.IsAvailable);
        Assert.False(vm.Volume.IsEnabled);
    }
}
