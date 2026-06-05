using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class MixerViewModelTests
{
    public MixerViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public void Crossfader_EmitsMixerCrossfade_Absolute()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MixerViewModel(dispatcher);

        vm.Crossfader.Value = 0.75;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerCrossfade, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(0.75, action.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ChannelGain_EmitsMixerChannelGain_ForItsSlot(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MixerViewModel(dispatcher);

        ContinuousControlViewModel fader = slot == 0 ? vm.ChannelGainA : vm.ChannelGainB;
        fader.Value = 0.4;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerChannelGain, action.Kind);
        Assert.Equal(slot, action.Slot);
        Assert.Equal(0.4, action.Value);
    }

    [Fact]
    public void Crossfader_SeedsFromFeedback()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(PerformanceActionKind.MixerCrossfade, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.2));

        var vm = new MixerViewModel(dispatcher);

        Assert.Equal(0.2, vm.Crossfader.Value);
    }

    [Fact]
    public void Feedback_UpdatesFader_WithoutReDispatching()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MixerViewModel(dispatcher);

        dispatcher.RaiseFeedback(PerformanceActionKind.MixerCrossfade, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.9));

        Assert.Equal(0.9, vm.Crossfader.Value);
        Assert.Empty(dispatcher.Dispatched); // echoed feedback must not loop
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Cue_EmitsMixerCueToggle_ForItsSlot(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MixerViewModel(dispatcher);

        await (slot == 0 ? vm.CueACommand : vm.CueBCommand).Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerCueToggle, action.Kind);
        Assert.Equal(slot, action.Slot);
    }

    [Fact]
    public void Cue_LatchesFromFeedback()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MixerViewModel(dispatcher);

        dispatcher.RaiseFeedback(PerformanceActionKind.MixerCueToggle, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        Assert.False(vm.IsCueA);
        Assert.True(vm.IsCueB);
    }
}
