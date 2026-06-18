using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Mixer;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Live.Modules;

public sealed class MixerViewModelTests
{
    private sealed class FakeLevelMeter : IDeckLevelMeter
    {
        public DeckLevel A { get; set; }
        public DeckLevel B { get; set; }

        public DeckLevel GetLevel(int slot) => slot == 0 ? A : B;
    }

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
    public void Channels_ExposeSuppliedDecks_AsMixerStrips()
    {
        var dispatcher = new FakeDispatcher();
        var deckA = new DeckViewModel(slot: 0, dispatcher);
        var deckB = new DeckViewModel(slot: 1, dispatcher);

        var vm = new MixerViewModel(dispatcher, channelA: deckA, channelB: deckB);

        Assert.Same(deckA, vm.ChannelA);
        Assert.Same(deckB, vm.ChannelB);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ChannelEq_EmitsMixerEqBand_ForItsDeckSlot(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var deckA = new DeckViewModel(slot: 0, dispatcher);
        var deckB = new DeckViewModel(slot: 1, dispatcher);
        var vm = new MixerViewModel(dispatcher, channelA: deckA, channelB: deckB);

        DeckViewModel channel = slot == 0 ? vm.ChannelA! : vm.ChannelB!;
        channel.EqHigh.Value = 0.7;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerEqBand, action.Kind);
        Assert.Equal(slot, action.Slot);
        Assert.Equal("High", action.Argument);
        Assert.Equal(0.7, action.Value);
    }

    [Fact]
    public void Channels_AreNull_WhenNoDecksSupplied()
    {
        var vm = new MixerViewModel(new FakeDispatcher());

        Assert.Null(vm.ChannelA);
        Assert.Null(vm.ChannelB);
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

    [Fact]
    public void CueLevel_EmitsMixerCueLevel_Absolute()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MixerViewModel(dispatcher);

        vm.CueLevel.Value = 0.6;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerCueLevel, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(0.6, action.Value);
    }

    [Fact]
    public void CueMix_EmitsMixerCueMix_Absolute()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MixerViewModel(dispatcher);

        vm.CueMix.Value = 0.35;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.MixerCueMix, action.Kind);
        Assert.Equal(0.35, action.Value);
    }

    [Fact]
    public void CueLevel_Feedback_UpdatesControl_WithoutReDispatching()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new MixerViewModel(dispatcher);

        dispatcher.RaiseFeedback(PerformanceActionKind.MixerCueMix, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.8));

        Assert.Equal(0.8, vm.CueMix.Value);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public void UpdateLevels_ReadsEachDeckPeak()
    {
        var meter = new FakeLevelMeter
        {
            A = new DeckLevel(0.75, 0.4),
            B = new DeckLevel(0.25, 0.1),
        };
        var vm = new MixerViewModel(levelMeter: meter);

        vm.UpdateLevels(deckAPlaying: true, deckBPlaying: true);

        Assert.Equal(0.75, vm.LevelA);
        Assert.Equal(0.25, vm.LevelB);
    }

    [Fact]
    public void UpdateLevels_ClearsStoppedDeck()
    {
        var meter = new FakeLevelMeter
        {
            A = new DeckLevel(0.75, 0.4),
            B = new DeckLevel(0.25, 0.1),
        };
        var vm = new MixerViewModel(levelMeter: meter);

        vm.UpdateLevels(deckAPlaying: true, deckBPlaying: true);
        vm.UpdateLevels(deckAPlaying: false, deckBPlaying: true);

        Assert.Equal(0, vm.LevelA);
        Assert.Equal(0.25, vm.LevelB);
    }
}
