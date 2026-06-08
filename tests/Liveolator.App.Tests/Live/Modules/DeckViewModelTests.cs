using System;
using System.ComponentModel;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Waveform;
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
    public void DeckControls_AreEnabled_WhenAnEngineBacksTheDeck()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());

        Assert.True(vm.CanCue);
        Assert.True(vm.CanLoop);
        Assert.True(vm.CanHotCue);
        Assert.True(vm.Pitch.IsEnabled);
        Assert.All(vm.HotCues, pad => Assert.True(pad.IsEnabled));
    }

    [Fact]
    public void DeckControls_AreDisabled_WithoutADispatcher()
    {
        var vm = new DeckViewModel(slot: 0); // catalog-browser mode: no engine backs the deck

        Assert.False(vm.CanCue);
        Assert.False(vm.CanLoop);
        Assert.False(vm.CanHotCue);
        Assert.False(vm.Pitch.IsEnabled);
        Assert.All(vm.HotCues, pad => Assert.False(pad.IsEnabled));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Cue_EmitsDeckCue_ForItsSlot(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot, dispatcher);

        await vm.CueCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckCue, action.Kind);
        Assert.Equal(slot, action.Slot);
    }

    [Fact]
    public async Task Loop_EmitsDeckSetLoop_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher);

        await vm.LoopCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSetLoop, action.Kind);
        Assert.Equal(1, action.Slot);
        Assert.True(action.Value > 0, "loop length (beats) should be positive");
    }

    [Fact]
    public void IsLooping_FollowsDeckSetLoopFeedback_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        Assert.False(vm.IsLooping);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetLoop, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.False(vm.IsLooping); // other deck

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetLoop, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.True(vm.IsLooping);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task HotCuePad_EmitsDeckHotCue_WithItsIndexAndSlot(int padIndex)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher);

        await vm.HotCues[padIndex].TriggerCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckHotCue, action.Kind);
        Assert.Equal(1, action.Slot);
        Assert.Equal(padIndex.ToString(), action.Argument);
    }

    [Fact]
    public void HotCuePad_LightsFromFeedback_ForItsIndex()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckHotCue, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: "2"));

        Assert.True(vm.HotCues[2].IsSet);
        Assert.False(vm.HotCues[0].IsSet);
    }

    [Fact]
    public void Pitch_EmitsDeckPitch_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher);

        vm.Pitch.Value = 0.65;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckPitch, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(1, action.Slot);
        Assert.Equal(0.65, action.Value);
    }

    [Fact]
    public void Bpm_EmitsDeckBpm_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(
            PerformanceActionKind.DeckBpm,
            slot: 1,
            new ActionFeedbackState(false, true, 120.0, "110.4|129.6"));
        var vm = new DeckViewModel(slot: 1, dispatcher);

        vm.Bpm = 126.5m;

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckBpm, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(1, action.Slot);
        Assert.Equal(126.5, action.Value, 6);
    }

    [Fact]
    public void Bpm_FeedbackUpdatesValueAndRange_WithoutRedispatching()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        dispatcher.RaiseFeedback(
            PerformanceActionKind.DeckBpm,
            slot: 0,
            new ActionFeedbackState(false, true, 128.0, "117.76|138.24"));

        Assert.True(vm.IsBpmEnabled);
        Assert.Equal(128.0m, vm.Bpm);
        Assert.Equal(117.76m, vm.MinimumBpm);
        Assert.Equal(138.24m, vm.MaximumBpm);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Theory]
    [InlineData(0.42)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public async Task Seek_EmitsDeckSeek_WithTheClickedFraction(double fraction)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        await vm.SeekCommand.Execute(fraction).ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSeek, action.Kind);
        Assert.Equal(ActionInputMode.Absolute, action.InputMode);
        Assert.Equal(0, action.Slot);
        Assert.Equal(fraction, action.Value);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.7, 1.0)]
    public async Task Seek_ClampsTheFraction_ToTheUnitRange(double input, double expected)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        await vm.SeekCommand.Execute(input).ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(expected, action.Value);
    }

    [Fact]
    public async Task Seek_IgnoresANonFiniteFraction()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        await vm.SeekCommand.Execute(double.NaN).ToTask();

        Assert.Empty(dispatcher.Dispatched);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Sync_EmitsDeckSyncOnce_ForItsSlot(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot, dispatcher);

        Assert.True(vm.CanSync);
        await vm.SyncCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSyncOnce, action.Kind);
        Assert.Equal(slot, action.Slot);
    }

    [Fact]
    public void CanSync_IsFalse_WithoutADispatcher()
    {
        var vm = new DeckViewModel(slot: 0); // catalog-browser mode: no engine backs the deck

        Assert.False(vm.CanSync);
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

    [Fact]
    public async Task BeatGrid_IsDerived_FromTheLoadBpmAndTheDecodedDuration()
    {
        var dispatcher = new FakeDispatcher();
        // 120 BPM over a 4 s overview = 0.5 s/beat → beats at 0,0.5,…,4 s → 9 lines (0..8).
        var provider = FakeWaveformProvider.WithDuration(durationSeconds: 4);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider);

        Task gridSet = WaitForBeatGrid(vm);
        // DeckLoadTrack feedback carries the path (Argument) and the analyzed BPM (Value).
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        await gridSet;

        Assert.Equal(9, vm.BeatGrid.Count);
        Assert.Equal(0.0, vm.BeatGrid[0], 6);
        Assert.Equal(1.0, vm.BeatGrid[8], 6);
    }

    [Fact]
    public async Task BeatGrid_AnchorsOnTheFirstBeat_FromDeckSetFirstBeatFeedback()
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithDuration(durationSeconds: 4);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider);

        Task gridSet = WaitForBeatGrid(vm);
        // The load carries the BPM; the downbeat anchor arrives right after via DeckSetFirstBeat feedback.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetFirstBeat, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 1.0)); // first beat at 1.0 s
        await gridSet;

        // 120 BPM, 4 s, anchor 1.0 s → the grid starts on the first beat at 1.0/4 = 0.25 (sits on the kick).
        Assert.Equal(0.25, vm.BeatGrid[0], 6);
    }

    [Fact]
    public async Task BeatGrid_StaysEmpty_WhenTheLoadReportsNoBpm()
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithDuration(durationSeconds: 4);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider);

        Task waveformSet = WaitForProperty(vm, nameof(DeckViewModel.Waveform));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: @"C:\song.flac"));
        await waveformSet;

        Assert.Empty(vm.BeatGrid);
    }

    [Fact]
    public void TrackLoad_PopulatesTitleAndMeta_FromResolver()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher,
            trackInfo: _ => new DeckTrackInfo(Title: "Midnight City", Bpm: "128.0", Key: "8A", Duration: "6:48"));

        Assert.False(vm.HasTrackMeta);
        Assert.Equal("—", vm.Meta);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0, Argument: @"C:\music\anything.mp3"));

        Assert.True(vm.HasTrackMeta);
        Assert.Equal("Midnight City", vm.Title);
        Assert.Equal("8A · 128.0 BPM · 6:48", vm.Meta);
    }

    [Fact]
    public void TrackLoad_WithoutResolver_FallsBackToFileName_AndNoMeta()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher); // no catalog resolver (e.g. Live tab)

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0, Argument: @"C:\music\My Track.mp3"));

        Assert.Equal("My Track", vm.Title);
        Assert.False(vm.HasTrackMeta);
        Assert.Equal("—", vm.Meta);
    }

    [Fact]
    public void TrackLoad_WithoutResolver_StillShowsBpm_FromTheLoadValue()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher); // no catalog resolver, but the load carries the BPM

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 126, Argument: @"C:\music\track.mp3"));

        // A deck must never hide its tempo: with no catalog entry the meta still shows the analyzed BPM.
        Assert.True(vm.HasTrackMeta);
        Assert.Equal("126.0 BPM", vm.Meta);
    }

    [Fact]
    public void BpmFeedback_AfterSync_UpdatesTheDisplayedTempo()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher,
            trackInfo: _ => new DeckTrackInfo("T", "120.0", "1A", "5:00"));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 1,
            new ActionFeedbackState(false, true, 120, @"C:\music\x.mp3"));

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, 1,
            new ActionFeedbackState(false, true, 132, "110.4|129.6"));

        Assert.Equal(132, vm.Bpm);
        Assert.Contains("132.0 BPM", vm.Meta);
        Assert.DoesNotContain("120.0 BPM", vm.Meta);
    }

    [Fact]
    public void TrackLoad_MetaIsSlotIsolated()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher,
            trackInfo: _ => new DeckTrackInfo("T", "120.0", "1A", "5:00"));

        // a load reported for deck B must not touch deck A
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0, Argument: @"C:\music\x.mp3"));

        Assert.False(vm.HasTrackMeta);
    }

    // --- Zoom-follow during playback (doc 22 — scrolling waveform for kick-sync by eye) ---

    [Fact]
    public void Play_ZoomsTheWaveform_AndPauseReturnsToOverview()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        Assert.Equal(0.0, vm.ZoomWindow, 6); // stopped → whole-track overview

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.True(vm.ZoomWindow > 0, "play should zoom the waveform into a follow window.");

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0));
        Assert.Equal(0.0, vm.ZoomWindow, 6); // pause → back to the overview
    }

    [Fact]
    public void UpdatePlayhead_FollowsLivePosition_OnlyWhilePlaying()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);
        dispatcher.SeedFeedback(PerformanceActionKind.DeckSeek, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.42));

        // stopped → the playhead must not move
        vm.UpdatePlayhead();
        Assert.Equal(0.0, vm.Progress, 6);

        // playing → the playhead follows the engine's live position (read through the feedback seam)
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckPlayPause, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        vm.UpdatePlayhead();
        Assert.Equal(0.42, vm.Progress, 6);
    }

    // The deck loads its overview off-thread (async void over Task.Run); wait for the property the load
    // sets rather than racing it. Times out so a regression fails fast instead of hanging.
    private static Task WaitForBeatGrid(DeckViewModel vm) => WaitForProperty(vm, nameof(DeckViewModel.BeatGrid));

    private static Task WaitForProperty(DeckViewModel vm, string propertyName)
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == propertyName)
            {
                vm.PropertyChanged -= Handler;
                tcs.TrySetResult();
            }
        }
        vm.PropertyChanged += Handler;
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
