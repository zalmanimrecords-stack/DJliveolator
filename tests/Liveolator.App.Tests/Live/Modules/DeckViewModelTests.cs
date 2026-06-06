using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class DeckViewModelTests
{
    public DeckViewModelTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    public async Task PlayPause_EmitsDeckPlayPause_ForItsSlot(int slot, string id)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot, dispatcher);

        Assert.Equal(id, vm.DeckId);
        await vm.PlayPauseCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckPlayPause, action.Kind);
        Assert.Equal(slot, action.Slot);
    }

    [Theory]
    [InlineData("High")]
    [InlineData("Mid")]
    [InlineData("Low")]
    public void EqKnob_EmitsMixerEqBand_WithBandArgumentAndSlot(string band)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher);

        ContinuousControlViewModel knob = band switch
        {
            "High" => vm.EqHigh,
            "Mid" => vm.EqMid,
            _ => vm.EqLow,
        };
        knob.Value = 0.7;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerEqBand, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(1, action.Slot);
        Assert.Equal(band, action.Argument);
        Assert.Equal(0.7, action.Value);
    }

    [Fact]
    public void Filter_EmitsMixerFilter_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        vm.Filter.Value = 0.2;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerFilter, action.Kind);
        Assert.Equal(0, action.Slot);
        Assert.Equal(0.2, action.Value);
    }

    [Fact]
    public void DeferredControls_AreDisabled()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());

        Assert.False(vm.CanCue);
        Assert.False(vm.CanLoop);
        Assert.False(vm.CanHotCue);
        Assert.False(vm.Pitch.IsEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Sync_EmitsDeckSyncLockToggle_ForItsSlot(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot, dispatcher);

        Assert.True(vm.CanSync);
        await vm.SyncCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSyncLockToggle, action.Kind);
        Assert.Equal(slot, action.Slot);
    }

    [Fact]
    public void CanSync_IsFalse_WithoutADispatcher()
    {
        var vm = new DeckViewModel(slot: 0); // catalog-browser mode: no engine backs the deck

        Assert.False(vm.CanSync);
    }

    [Fact]
    public void IsSyncLocked_FollowsDeckSyncLockToggleFeedback_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher);

        Assert.False(vm.IsSyncLocked);

        // Feedback for the other deck must not affect this one.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSyncLockToggle, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.False(vm.IsSyncLocked);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSyncLockToggle, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.True(vm.IsSyncLocked);
    }

    [Fact]
    public void IsPlaying_FollowsDeckPlayPauseFeedback_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher);

        Assert.False(vm.IsPlaying);

        // Feedback for the other deck must not affect this one.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.False(vm.IsPlaying);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.True(vm.IsPlaying);
    }
}
