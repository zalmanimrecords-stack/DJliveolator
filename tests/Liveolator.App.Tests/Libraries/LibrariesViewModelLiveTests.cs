using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Actions;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Libraries;

public sealed class LibrariesViewModelLiveTests
{
    public LibrariesViewModelLiveTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private sealed class RecordingDispatcher : IPerformanceActionDispatcher
    {
        public RecordingDispatcher(int deckCount = 1) => DeckCount = deckCount;

        public int DeckCount { get; }
        public List<PerformanceAction> Dispatched { get; } = new();
        public void Dispatch(PerformanceAction action) => Dispatched.Add(action);

        // Mirrors DeckActionHandler.GetFeedback: a deck slot is available iff slot < DeckCount.
        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => kind == PerformanceActionKind.DeckPlayPause && slot >= 0 && slot < DeckCount
                ? new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0)
                : ActionFeedbackState.Unavailable;

        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged { add { } remove { } }
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

    private static LibrariesViewModel BuildLiveViewModel(
        RecordingDispatcher dispatcher, FakeBeatClock clock, params string[] files)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(files), new FakeAudioDecoder());
        var vm = new LibrariesViewModel(library, dispatcher, clock);
        vm.AddFolder("/music");
        return vm;
    }

    [Fact]
    public void LiveModeDisabled_WhenNoDispatcher()
    {
        var library = new MusicLibrary(new FakeFileEnumerator("/music/Alpha.wav"), new FakeAudioDecoder());
        var vm = new LibrariesViewModel(library);

        Assert.False(vm.IsLiveModeEnabled);
    }

    [Fact]
    public void LiveModeEnabled_WhenDispatcherProvided()
    {
        var vm = BuildLiveViewModel(new RecordingDispatcher(), new FakeBeatClock(), "/music/Alpha.wav");

        Assert.True(vm.IsLiveModeEnabled);
    }

    [Fact]
    public async Task PlaySelected_DispatchesLoadThenPlayPause_ForSelectedTrackPath()
    {
        var dispatcher = new RecordingDispatcher();
        var vm = BuildLiveViewModel(dispatcher, new FakeBeatClock(), "/music/Alpha.wav");
        await vm.ScanCommand.Execute().ToTask();
        vm.SelectedTrack = vm.Tracks[0];

        await vm.PlaySelectedCommand.Execute().ToTask();

        Assert.Equal(2, dispatcher.Dispatched.Count);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, dispatcher.Dispatched[0].Kind);
        Assert.Contains("Alpha.wav", dispatcher.Dispatched[0].Argument);
        Assert.Equal(PerformanceActionKind.DeckPlayPause, dispatcher.Dispatched[1].Kind);
    }

    [Fact]
    public async Task Stop_DispatchesTransportStop()
    {
        var dispatcher = new RecordingDispatcher();
        var vm = BuildLiveViewModel(dispatcher, new FakeBeatClock(), "/music/Alpha.wav");

        await vm.StopCommand.Execute().ToTask();

        Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.TransportStop, dispatcher.Dispatched[0].Kind);
    }

    [Fact]
    public async Task LoadToDeckA_DispatchesDeckLoadTrack_Slot0_WithPath_AndDoesNotPlay()
    {
        var dispatcher = new RecordingDispatcher(deckCount: 1);
        var vm = BuildLiveViewModel(dispatcher, new FakeBeatClock(), "/music/Alpha.wav");
        await vm.ScanCommand.Execute().ToTask();
        vm.SelectedTrack = vm.Tracks[0];

        await vm.LoadToDeckACommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, action.Kind); // load only — no DeckPlayPause
        Assert.Equal(0, action.Slot);
        Assert.Contains("Alpha.wav", action.Argument);
        Assert.Contains("Deck A", vm.LoadStatus);
    }

    [Fact]
    public async Task LoadToDeckB_DispatchesSlot1_WhenTwoDecksAvailable()
    {
        var dispatcher = new RecordingDispatcher(deckCount: 2);
        var vm = BuildLiveViewModel(dispatcher, new FakeBeatClock(), "/music/Alpha.wav");
        await vm.ScanCommand.Execute().ToTask();
        vm.SelectedTrack = vm.Tracks[0];

        await vm.LoadToDeckBCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckLoadTrack, action.Kind);
        Assert.Equal(1, action.Slot);
    }

    [Fact]
    public void CanLoadToDeckB_FalseWithOneDeck_TrueWithTwoDecks()
    {
        var oneDeck = BuildLiveViewModel(new RecordingDispatcher(deckCount: 1), new FakeBeatClock(), "/music/A.wav");
        var twoDeck = BuildLiveViewModel(new RecordingDispatcher(deckCount: 2), new FakeBeatClock(), "/music/A.wav");

        Assert.True(oneDeck.CanLoadToDeckA);
        Assert.False(oneDeck.CanLoadToDeckB);
        Assert.True(twoDeck.CanLoadToDeckB);
    }

    [Fact]
    public void CanLoadToDeck_FalseWithoutDispatcher()
    {
        var library = new MusicLibrary(new FakeFileEnumerator("/music/A.wav"), new FakeAudioDecoder());
        var vm = new LibrariesViewModel(library);

        Assert.False(vm.CanLoadToDeckA);
        Assert.False(vm.CanLoadToDeckB);
    }

    [Fact]
    public void LiveBpm_UpdatesFromBeatClock()
    {
        var clock = new FakeBeatClock();
        var vm = BuildLiveViewModel(new RecordingDispatcher(), clock, "/music/Alpha.wav");

        Assert.Equal("—", vm.LiveBpm);
        clock.Publish(BeatClockState.Idle with { Bpm = 128.0 });

        Assert.Contains("128", vm.LiveBpm);
    }
}
