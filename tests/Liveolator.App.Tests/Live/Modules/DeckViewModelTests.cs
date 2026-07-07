using System;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Live.Modules;
using Liveolator.App.Tests.Live;
using System.Linq;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
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
    public void Load_SetsArtistFromCatalog_AndShowsTheArtistLine()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher,
            trackInfo: _ => new DeckTrackInfo("Life System", "138.0", "8A", "7:12", Artist: "Astrix"));

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 138, Argument: @"C:\life.flac"));

        Assert.Equal("Astrix", vm.Artist);
        Assert.True(vm.HasArtist);
    }

    [Fact]
    public void Load_WithNoCatalogArtist_LeavesTheArtistLineHidden()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher,
            trackInfo: _ => new DeckTrackInfo("Untitled", "128.0", "—", "4:00")); // catalog has no artist

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 128, Argument: @"C:\x.flac"));

        Assert.Null(vm.Artist);
        Assert.False(vm.HasArtist);
    }

    [Fact]
    public void FailedLoad_ClearsTheArtist()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher,
            trackInfo: _ => new DeckTrackInfo("Life System", "138.0", "8A", "7:12", Artist: "Astrix"));

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 138, Argument: @"C:\life.flac"));
        Assert.True(vm.HasArtist);

        // A later load the engine could not complete clears the artist line (no stale artist on an empty deck).
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: false, Value: 0, Argument: @"S:\gone.flac"));

        Assert.Null(vm.Artist);
        Assert.False(vm.HasArtist);
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

    [Theory]
    [InlineData("High", 0.2)]
    [InlineData("Mid", 0.4)]
    [InlineData("Low", 0.8)]
    public void EqFeedback_UpdatesMatchingKnob(string band, double value)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        dispatcher.RaiseFeedback(
            PerformanceActionKind.MixerEqBand,
            slot: 0,
            new ActionFeedbackState(false, true, value, band));

        ContinuousControlViewModel knob = band switch
        {
            "High" => vm.EqHigh,
            "Mid" => vm.EqMid,
            _ => vm.EqLow,
        };
        Assert.Equal(value, knob.Value);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public async Task ResetEq_SnapsAllThreeBandsToFlat_AndReemitsForEachMovedBand()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher);

        vm.EqHigh.Value = 0.9;
        vm.EqMid.Value = 0.1;
        vm.EqLow.Value = 0.3;
        dispatcher.Dispatched.Clear();

        await vm.ResetEqCommand.Execute().ToTask();

        Assert.Equal(0.5, vm.EqHigh.Value);
        Assert.Equal(0.5, vm.EqMid.Value);
        Assert.Equal(0.5, vm.EqLow.Value);

        Assert.Equal(3, dispatcher.Dispatched.Count);
        Assert.All(dispatcher.Dispatched, action =>
        {
            Assert.Equal(PerformanceActionKind.MixerEqBand, action.Kind);
            Assert.Equal(1, action.Slot);
            Assert.Equal(0.5, action.Value);
        });
    }

    [Fact]
    public async Task ResetEq_LeavesTheFilterUntouched()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        vm.Filter.Value = 0.2;
        dispatcher.Dispatched.Clear();

        await vm.ResetEqCommand.Execute().ToTask();

        Assert.Equal(0.2, vm.Filter.Value);
        Assert.DoesNotContain(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.MixerFilter);
    }

    [Fact]
    public void ResetEqCommand_IsDisabled_WithoutADispatcher()
    {
        var vm = new DeckViewModel(slot: 0); // catalog-browser mode: no mixer handler backs the EQ

        bool canReset = true;
        using (vm.ResetEqCommand.CanExecute.Subscribe(v => canReset = v)) { }
        Assert.False(canReset);
    }

    [Fact]
    public void DeckControls_AreEnabled_OnceATrackLoads_WhenAnEngineBacksTheDeck()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        // Engine backs the deck, but nothing is loaded yet → the transport controls are disabled, not
        // dead buttons that silently do nothing when pressed (the owner's SYNC report).
        Assert.False(vm.CanCue);
        Assert.False(vm.CanLoop);
        Assert.False(vm.CanHotCue);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 128, Argument: @"C:\song.flac"));

        Assert.True(vm.CanCue);
        Assert.True(vm.CanLoop);
        Assert.True(vm.CanHotCue);
        Assert.True(vm.Pitch.IsEnabled); // the pitch control is engine-gated, not track-gated
        Assert.All(vm.HotCues, pad => Assert.True(pad.IsEnabled));
    }

    [Fact]
    public void TransportControls_StayDisabled_WhenAnEngineBacksTheDeckButNoTrackLoads()
    {
        // Reproduces the dead-button half of the owner's report: a deck whose load FAILED (the engine
        // never raised DeckLoadTrack feedback) must keep SYNC/CUE/LOOP/NUDGE disabled, so pressing them
        // can't read as "the button is broken".
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        Assert.False(vm.CanCue);
        Assert.False(vm.CanLoop);
        Assert.False(vm.CanHotCue);
        Assert.False(vm.CanSync);
        Assert.False(vm.CanNudgeSeek);
        Assert.False(vm.CanPitchBend);
    }

    [Fact]
    public void CanSync_TracksTheDecksBpm_NotJustTheLoad()
    {
        // SYNC needs an analyzed tempo to beatmatch against. A track that loads WITHOUT a BPM (Value 0)
        // must leave SYNC disabled — there is nothing to equalize — even though the deck now has a track.
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);
        Assert.False(vm.CanSync);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: @"C:\nobpm.flac"));
        Assert.False(vm.CanSync); // loaded, but no BPM → still nothing to sync to

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 128, Argument: "117.76|138.24"));
        Assert.True(vm.CanSync); // BPM now known → SYNC becomes available
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

    [Fact]
    public void DeckTransport_IsDisabled_WhenNoDeckEngineBacksTheDeck_ButMixerEqStaysLive()
    {
        // Catalog-browser mode (no realtime audio): a dispatcher exists for the always-present mixer/visual
        // handlers, but DeckActionHandler is absent, so transport actions would be silently DROPPED. The
        // deck must present the transport controls disabled (not enabled-but-inert) — QA finding S1.
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher, deckTransportEnabled: false);

        Assert.False(vm.IsEnabled);
        Assert.False(vm.CanCue);
        Assert.False(vm.CanLoop);
        Assert.False(vm.CanHotCue);
        Assert.False(vm.CanSync);
        Assert.False(vm.CanNudgeSeek);
        Assert.False(vm.CanPitchBend);
        Assert.False(vm.Pitch.IsEnabled);
        Assert.All(vm.HotCues, pad => Assert.False(pad.IsEnabled));

        // A disabled command must not dispatch — proving the action can't be silently dropped.
        bool canPlay = true;
        using (vm.PlayPauseCommand.CanExecute.Subscribe(v => canPlay = v)) { }
        Assert.False(canPlay);

        // The EQ/filter knobs are owned by the always-present MixerActionHandler, so they remain usable.
        Assert.True(vm.EqHigh.IsEnabled);
        Assert.True(vm.EqMid.IsEnabled);
        Assert.True(vm.EqLow.IsEnabled);
        Assert.True(vm.Filter.IsEnabled);
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
    public async Task ExitLoop_EmitsDeckSetLoopWithZeroBeats_ForItsSlot()
    {
        // The release button clears any active loop by emitting DeckSetLoop with a non-positive
        // beat length, which the engine handler maps to ClearLoop (doc 11).
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher);

        await vm.ExitLoopCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSetLoop, action.Kind);
        Assert.Equal(1, action.Slot);
        Assert.True(action.Value <= 0, "exit-loop must carry a non-positive beat length so the engine clears the loop");
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
    [InlineData(1)]
    public async Task KeyLock_EmitsDeckKeyLockToggle_ForItsSlot(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot, dispatcher);

        await vm.KeyLockCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckKeyLockToggle, action.Kind);
        Assert.Equal(slot, action.Slot);
    }

    [Fact]
    public void IsKeyLock_FollowsDeckKeyLockToggleFeedback_ForItsSlot()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        Assert.False(vm.IsKeyLock);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckKeyLockToggle, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.False(vm.IsKeyLock); // other deck

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckKeyLockToggle, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));
        Assert.True(vm.IsKeyLock);
    }

    [Fact]
    public void IsKeyLock_SeedsFromExistingFeedback_AtConstruction()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(PerformanceActionKind.DeckKeyLockToggle, slot: 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        var vm = new DeckViewModel(slot: 1, dispatcher);

        Assert.True(vm.IsKeyLock);
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
    public void Deck_Exposes_EightHotCues_AcrossTwoBanks()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());

        // Audit finding #2: all 8 auto-cue slots must be reachable, not just the first four.
        Assert.Equal(8, vm.HotCues.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, vm.VisibleHotCues.Select(p => p.Index)); // bank A by default
        Assert.Equal("A", vm.HotCueBankLabel);
    }

    [Fact]
    public void ToggleHotCueBank_SwapsTheVisiblePadsToBankB()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());

        vm.ToggleHotCueBankCommand.Execute().Subscribe();

        Assert.True(vm.IsHotCueBankB);
        Assert.Equal("B", vm.HotCueBankLabel);
        Assert.Equal(new[] { 4, 5, 6, 7 }, vm.VisibleHotCues.Select(p => p.Index)); // bank B (slots 5-8)
    }

    [Fact]
    public void HotCuePad_ShowsCueLabelColorAndAuto_FromFeedback()
    {
        // Audit finding #3: a pad must show the cue's name/color and mark suggestions, not just light a number.
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);
        string argument = HotCueFeedback.Encode(
            2, new HotCueInfo(IsSet: true, Label: "Drop", Color: 0xFF3B30, IsAuto: true));

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckHotCue, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: argument));

        HotCuePadViewModel pad = vm.HotCues[2];
        Assert.True(pad.IsSet);
        Assert.Equal("Drop", pad.CueLabel);
        Assert.Equal("Drop", pad.DisplayText);
        Assert.Equal(0xFF3B30, pad.Color);
        Assert.True(pad.IsAuto);
    }

    [Fact]
    public void HotCuePad_WithNoCueLabel_DisplaysItsNumber()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());

        Assert.Equal("1", vm.HotCues[0].DisplayText); // 1-based pad number fallback
        Assert.Equal("8", vm.HotCues[7].DisplayText);
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
    [InlineData(true, 0.1 / 4.0)]    // forward one default nudge step (0.1 s) of a 4 s track
    [InlineData(false, -0.1 / 4.0)]  // back 0.1 s
    public async Task SeekNudge_WithKnownDuration_EmitsRelativeDeckSeek_ByTheNudgeStep(bool forward, double expectedFraction)
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithDuration(durationSeconds: 4);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider); // default nudge step = 0.1 s

        // Load so the overview decodes and the deck learns the 4 s duration (the nudge needs it to convert).
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        await gridSet;
        dispatcher.Dispatched.Clear();

        await (forward ? vm.SeekForwardCommand : vm.SeekBackCommand).Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSeek, action.Kind);
        Assert.Equal(ActionInputMode.Relative, action.InputMode);
        Assert.Equal(0, action.Slot);
        Assert.Equal(expectedFraction, action.Value, precision: 6);
    }

    [Fact]
    public async Task SeekNudge_WithUnknownDuration_DoesNothing()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher); // no waveform provider → duration unknown

        await vm.SeekForwardCommand.Execute().ToTask();
        await vm.SeekBackCommand.Execute().ToTask();

        Assert.Empty(dispatcher.Dispatched); // no guessed jump until the track length is known
    }

    [Fact]
    public async Task SetNudgeSeconds_ChangesTheStepAppliedPerPress()
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithDuration(durationSeconds: 4);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider);
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        await gridSet;
        dispatcher.Dispatched.Clear();

        vm.SetNudgeSeconds(0.5); // a coarser step from Settings
        await vm.SeekForwardCommand.Execute().ToTask();

        Assert.Equal(0.5 / 4.0, Assert.Single(dispatcher.Dispatched).Value, precision: 6);
    }

    [Fact]
    public async Task NudgeBend_EmitsAMomentaryPitchBend_SignedByDirection()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        await vm.NudgeBendUpCommand.Execute().ToTask();
        PerformanceAction up = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckPitchBend, up.Kind);
        Assert.Equal(0, up.Slot);
        Assert.True(up.Value > 0, "bend up speeds the deck (positive rate fraction)");

        dispatcher.Dispatched.Clear();
        await vm.NudgeBendDownCommand.Execute().ToTask();
        PerformanceAction down = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckPitchBend, down.Kind);
        Assert.True(down.Value < 0, "bend down slows the deck (negative rate fraction)");
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

        // SYNC is available once the deck has an analyzed tempo to match against.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, slot,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 128, Argument: "117.76|138.24"));
        Assert.True(vm.CanSync);
        await vm.SyncCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSyncOnce, action.Kind);
        Assert.Equal(ActionInputMode.Momentary, action.InputMode);
        Assert.Equal(slot, action.Slot);
    }

    [Fact]
    public async Task Sync_IsOneShot_EachPressEmitsAFreshDeckSyncOnce()
    {
        // SYNC is a momentary beatmatch, not a latch: pressing it twice fires two independent
        // DeckSyncOnce actions (a toggle would have alternated engage/release state instead).
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        await vm.SyncCommand.Execute().ToTask();
        await vm.SyncCommand.Execute().ToTask();

        Assert.Equal(2, dispatcher.Dispatched.Count);
        Assert.All(dispatcher.Dispatched, a => Assert.Equal(PerformanceActionKind.DeckSyncOnce, a.Kind));
    }

    [Fact]
    public void CanSync_IsFalse_WithoutADispatcher()
    {
        var vm = new DeckViewModel(slot: 0); // catalog-browser mode: no engine backs the deck

        Assert.False(vm.CanSync);
    }

    [Fact]
    public void LoadFailedFeedback_ShowsTheFailure_AndKeepsTransportDisabled()
    {
        // The handler raises a load-FAILED feedback (IsAvailable:false) when the engine can't open the
        // track. The deck must show the failure and keep SYNC/CUE/etc disabled — not a silently empty
        // deck with dead buttons (the owner's report).
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        // A prior successful load enabled the controls...
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 128, Argument: @"C:\ok.flac"));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 128, Argument: "117.76|138.24"));
        Assert.True(vm.CanSync);

        // ...then a later load fails.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: false, Value: 0, Argument: @"S:\offline.flac"));

        Assert.False(vm.HasLoadedTrack);
        Assert.False(vm.CanSync);
        Assert.False(vm.CanCue);
        Assert.False(vm.CanLoop);
        Assert.Contains("offline", vm.Title, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.HasTrackMeta);
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
    public async Task Load_PopulatesAllThreeBandPeaks_FromTheOverview()
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithBands(durationSeconds: 4);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider);

        Task highSet = WaitForProperty(vm, nameof(DeckViewModel.HighPeaks));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        await highSet;

        // All three bands feed the layered strip (kick in front, mid body, high caps).
        Assert.NotNull(vm.KickPeaks);
        Assert.NotNull(vm.MidPeaks);
        Assert.NotNull(vm.HighPeaks);
    }

    [Fact]
    public async Task Load_ClearsPreviousBandPeaks_WhileTheNewOverviewDecodes()
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithBands(durationSeconds: 4);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider);

        Task highSet = WaitForProperty(vm, nameof(DeckViewModel.HighPeaks));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\first.flac"));
        await highSet;

        // A second load must show the empty state (no stale bands from the previous track) until the
        // new overview lands — same contract the broadband waveform already keeps. The null transition
        // of each band raises PropertyChanged before the new overview repopulates it.
        bool clearedDuringLoad = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeckViewModel.HighPeaks) && vm.HighPeaks is null &&
                vm.KickPeaks is null && vm.MidPeaks is null)
            {
                clearedDuringLoad = true;
            }
        };
        // Wait for the REPOPULATED value — the load first raises HighPeaks with null (the clear this
        // test is about), so a wait on the first change would complete too early.
        Task reloaded = WaitForHighPeaksValue(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\second.flac"));
        await reloaded;

        Assert.True(clearedDuringLoad, "band peaks should be nulled at load start (empty state, no stale strip)");
        Assert.NotNull(vm.HighPeaks);
    }

    private static Task WaitForHighPeaksValue(DeckViewModel vm)
    {
        var tcs = new TaskCompletionSource();
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeckViewModel.HighPeaks) && vm.HighPeaks is not null)
            {
                vm.PropertyChanged -= Handler;
                tcs.TrySetResult();
            }
        }
        vm.PropertyChanged += Handler;
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
    public void Constructor_RestoresTrackFromExistingLoadFeedback()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(
            PerformanceActionKind.DeckLoadTrack,
            1,
            new ActionFeedbackState(
                IsActive: true,
                IsAvailable: true,
                Value: 126,
                Argument: @"C:\music\Restored Track.mp3"));

        var vm = new DeckViewModel(slot: 1, dispatcher);

        Assert.Equal("Restored Track", vm.Title);
        Assert.Equal("126.0 BPM", vm.Meta);
        Assert.True(vm.HasTrackMeta);
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

    // --- Musical key readout ---

    [Fact]
    public void TrackLoad_SurfacesMusicalKey_FromResolver()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher,
            trackInfo: _ => new DeckTrackInfo(Title: "Midnight City", Bpm: "128.0", Key: "8A", Duration: "6:48"));

        Assert.Null(vm.TrackKey);
        Assert.False(vm.HasTrackKey);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 128, Argument: @"C:\music\track.mp3"));

        Assert.Equal("8A", vm.TrackKey);
        Assert.True(vm.HasTrackKey);
    }

    [Fact]
    public void TrackLoad_WithoutCatalogKey_LeavesKeyCleared()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher); // no catalog resolver → no key

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 126, Argument: @"C:\music\track.mp3"));

        Assert.Null(vm.TrackKey);
        Assert.False(vm.HasTrackKey);
    }

    [Fact]
    public void TrackLoad_WithoutKeyAfterAKnownKey_ClearsThePreviousKey()
    {
        var dispatcher = new FakeDispatcher();
        // First load carries a key; the second (a track missing from the catalog) must clear it.
        bool firstLoad = true;
        var vm = new DeckViewModel(slot: 0, dispatcher,
            trackInfo: _ =>
            {
                if (!firstLoad) return null;
                firstLoad = false;
                return new DeckTrackInfo(Title: "T", Bpm: "128.0", Key: "8A", Duration: "6:48");
            });

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 128, Argument: @"C:\music\a.mp3"));
        Assert.Equal("8A", vm.TrackKey);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 126, Argument: @"C:\music\b.mp3"));

        Assert.Null(vm.TrackKey); // no stale key from the previous track
        Assert.False(vm.HasTrackKey);
    }

    // --- Signed pitch-percent readout ---

    [Fact]
    public void PitchPercentText_IsZeroAtCentre()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());

        // The pitch fader seeds at centre (0.5) → no offset.
        Assert.Equal(0.5, vm.Pitch.Value, precision: 6);
        Assert.Equal("0.0%", vm.PitchPercentText);
    }

    [Theory]
    [InlineData(1.0, "+8.0%")]   // full up = the engine's +8% range maximum
    [InlineData(0.0, "-8.0%")]   // full down = -8%
    [InlineData(0.65, "+2.4%")]  // (0.65-0.5)*2*8% = +2.4%
    [InlineData(0.25, "-4.0%")]  // (0.25-0.5)*2*8% = -4.0%
    public void PitchPercentText_FormatsSignedPercent_AcrossTheRange(double pitchValue, string expected)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        vm.Pitch.Value = pitchValue;

        Assert.Equal(expected, vm.PitchPercentText);
    }

    [Fact]
    public void PitchPercentText_TracksPitchValueChanges()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        Assert.Equal("0.0%", vm.PitchPercentText);

        // A controller/feedback move pushes the value in without re-emitting; the readout still follows.
        vm.Pitch.SetFromFeedback(0.75);
        Assert.Equal("+4.0%", vm.PitchPercentText);

        vm.Pitch.SetFromFeedback(0.5);
        Assert.Equal("0.0%", vm.PitchPercentText);
    }

    [Fact]
    public void PitchPercentText_RaisesPropertyChanged_WhenPitchMoves()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.PitchPercentText)) raised = true; };

        vm.Pitch.Value = 0.6;

        Assert.True(raised);
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

    // --- Waveform zoom (doc 22 — see/align kicks while cued, via the shared ZOOM knob) ---

    [Fact]
    public async Task WaveformZoom_AppliesEvenWhenPaused_SoKicksResolveForAlignment()
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithDuration(durationSeconds: 40);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider, waveformZoomSeconds: 8.0);

        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        await gridSet;

        Assert.False(vm.IsPlaying);                 // paused / cued...
        Assert.Equal(8.0 / 40.0, vm.ZoomWindow, 6); // ...yet the waveform is zoomed to the 8 s window
    }

    [Fact]
    public async Task SetWaveformZoomSeconds_TighterWindow_ZoomsInFurther_AndZeroIsOverview()
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithDuration(durationSeconds: 40);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider);

        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        await gridSet;

        vm.SetWaveformZoomSeconds(4);
        Assert.Equal(4.0 / 40.0, vm.ZoomWindow, 6);
        vm.SetWaveformZoomSeconds(8);
        Assert.Equal(8.0 / 40.0, vm.ZoomWindow, 6); // wider window = less zoom

        vm.SetWaveformZoomSeconds(0);               // knob fully out
        Assert.Equal(0.0, vm.ZoomWindow, 6);        // whole-track overview
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

    // --- Elapsed / remaining time readout ---

    [Fact]
    public void TimeReadout_ShowsPlaceholders_BeforeATrackLoads()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());

        Assert.Equal("--:--", vm.ElapsedText);
        Assert.Equal("--:--", vm.RemainingText);
    }

    [Fact]
    public async Task TimeReadout_TracksThePlayhead_OnceTheDurationIsKnown()
    {
        var dispatcher = new FakeDispatcher();
        var provider = FakeWaveformProvider.WithDuration(durationSeconds: 240);
        var vm = new DeckViewModel(slot: 0, dispatcher, provider);

        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        await gridSet;

        Assert.Equal("0:00", vm.ElapsedText);
        Assert.Equal("-4:00", vm.RemainingText);

        // The playhead lands at 25% (seek feedback) → 1:00 elapsed of 4:00, 3:00 remaining.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSeek, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.25));

        Assert.Equal("1:00", vm.ElapsedText);
        Assert.Equal("-3:00", vm.RemainingText);
    }

    [Fact]
    public void TimeReadout_StaysOnPlaceholders_WhileTheDurationIsUnknown()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher); // no waveform provider → duration never decodes

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120, Argument: @"C:\song.flac"));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSeek, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.5));

        // No guessed time without a real duration (the readout must never invent numbers).
        Assert.Equal("--:--", vm.ElapsedText);
        Assert.Equal("--:--", vm.RemainingText);
    }

    // --- Self-heal: a load without BPM pulls the grid from the catalog ---

    [Fact]
    public void LoadWithoutBpm_SelfHealsTheGridFromTheCatalog()
    {
        var dispatcher = new FakeDispatcher();
        var analysis = new Liveolator.Core.Analysis.Bpm.BpmResult(140.0, 0.8, 0.29);
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(120),
            trackInfo: null, analysisInfo: _ => analysis);

        // A restored deck whose saved session predates analysis loads with BPM = 0. The load feedback is
        // handled synchronously, so the self-heal grid actions are emitted by the time this returns.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: @"C:\a.flac"));

        // The deck re-emits the catalog's grid through the grid actions instead of staying blank.
        Assert.Contains(dispatcher.Dispatched, a =>
            a.Kind == PerformanceActionKind.DeckSetGridBpm && Math.Abs(a.Value - 140.0) < 1e-9 && a.Slot == 0);
        Assert.Contains(dispatcher.Dispatched, a =>
            a.Kind == PerformanceActionKind.DeckSetFirstBeat && Math.Abs(a.Value - 0.29) < 1e-9 && a.Slot == 0);
    }

    [Fact]
    public void LoadWithBpm_DoesNotSelfHeal_TrustsTheLoadValue()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(120),
            trackInfo: null, analysisInfo: _ => new Liveolator.Core.Analysis.Bpm.BpmResult(140.0, 0.8, 0.29));

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 128.0, Argument: @"C:\a.flac"));

        // The load already carried a BPM, so no self-heal grid actions are emitted.
        Assert.DoesNotContain(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetGridBpm);
    }

    // --- Background BPM analysis: a load with no analysis anywhere self-heals by analyzing the file ---

    [Fact]
    public async Task LoadWithoutAnyAnalysis_RunsBackgroundAnalysis_AndDispatchesTheGridOnCompletion()
    {
        var dispatcher = new FakeDispatcher();
        var analysis = new Liveolator.Core.Analysis.Bpm.BpmResult(128.0, 0.9, FirstBeatSeconds: 0.31)
        {
            DownbeatSeconds = 0.81,
            DownbeatConfidence = 0.9,
        };
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(120),
            trackInfo: null, analysisInfo: _ => null, // not in the catalog at all
            bpmAnalysis: (_, _) => Task.FromResult<Liveolator.Core.Analysis.Bpm.BpmResult?>(analysis));
        Task<PerformanceAction> downbeat = WaitForDispatch(dispatcher, PerformanceActionKind.DeckSetDownbeat);

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: @"C:\new.flac"));
        await downbeat; // the downbeat is dispatched last, so all grid actions are in by now

        Assert.Contains(dispatcher.Dispatched, a =>
            a.Kind == PerformanceActionKind.DeckSetGridBpm && Math.Abs(a.Value - 128.0) < 1e-9 && a.Slot == 0);
        Assert.Contains(dispatcher.Dispatched, a =>
            a.Kind == PerformanceActionKind.DeckSetFirstBeat && Math.Abs(a.Value - 0.31) < 1e-9 && a.Slot == 0);
        PerformanceAction one = await downbeat; // already completed above — just reads the result
        Assert.Equal(0.81, one.Value, precision: 6);
        Assert.Equal(DeckViewModel.AnalysisOrigin, one.Origin); // never persisted as a manual edit
    }

    [Fact]
    public async Task LoadWithCatalogAnalysis_DoesNotRunBackgroundAnalysis()
    {
        var dispatcher = new FakeDispatcher();
        int analysisRuns = 0;
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(120),
            trackInfo: null,
            analysisInfo: _ => new Liveolator.Core.Analysis.Bpm.BpmResult(140.0, 0.8, 0.29),
            bpmAnalysis: (_, _) =>
            {
                Interlocked.Increment(ref analysisRuns);
                return Task.FromResult<Liveolator.Core.Analysis.Bpm.BpmResult?>(null);
            });

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: @"C:\a.flac"));
        // The catalog self-heal grids the deck synchronously; a wrongly-started analysis would run on a
        // worker thread right after, so give it a moment to (not) appear.
        await Task.Delay(100);

        Assert.Equal(0, analysisRuns); // the catalog already gridded the deck — never decode twice
    }

    [Fact]
    public async Task TrackSwap_CancelsTheInFlightAnalysis_AndNeverAppliesTheStaleResult()
    {
        var dispatcher = new FakeDispatcher();
        var staleResult = new TaskCompletionSource<Liveolator.Core.Analysis.Bpm.BpmResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var analysisStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(120),
            trackInfo: null, analysisInfo: _ => null,
            bpmAnalysis: (_, token) =>
            {
                analysisStarted.TrySetResult(token);
                return staleResult.Task;
            });

        // Track A loads unanalyzed → analysis starts; track B (analyzed) replaces it before it finishes.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: @"C:\a.flac"));
        CancellationToken analysisToken = await analysisStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 128.0, Argument: @"C:\b.flac"));

        Assert.True(analysisToken.IsCancellationRequested); // the swap cancelled A's analysis

        // A's late result must not re-grid B — give the (cancelled) continuation a moment to misbehave.
        staleResult.SetResult(new Liveolator.Core.Analysis.Bpm.BpmResult(99.0, 0.9, 0.5));
        await Task.Delay(100);
        Assert.DoesNotContain(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetGridBpm);
    }

    [Fact]
    public async Task DuplicateLoadOfTheSameTrack_DoesNotStartASecondAnalysis()
    {
        var dispatcher = new FakeDispatcher();
        int analysisRuns = 0;
        var pending = new TaskCompletionSource<Liveolator.Core.Analysis.Bpm.BpmResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(120),
            trackInfo: null, analysisInfo: _ => null,
            bpmAnalysis: (_, _) =>
            {
                Interlocked.Increment(ref analysisRuns);
                return pending.Task;
            });
        var load = new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: @"C:\a.flac");

        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0, load);
        // Wait until the first analysis is actually underway (it starts on a worker thread).
        await WaitUntil(() => Volatile.Read(ref analysisRuns) > 0);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0, load); // duplicate load feedback
        await Task.Delay(100);

        Assert.Equal(1, analysisRuns);
        pending.SetResult(null); // release the in-flight task
    }

    [Fact]
    public async Task BackgroundAnalysisFailure_IsLoggedAndNeverThrown()
    {
        var dispatcher = new FakeDispatcher();
        var logged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new WarningCaptureListener(logged);
        System.Diagnostics.Trace.Listeners.Add(listener);
        try
        {
            var vm = new DeckViewModel(
                slot: 0, dispatcher, FakeWaveformProvider.WithDuration(120),
                trackInfo: null, analysisInfo: _ => null,
                bpmAnalysis: (_, _) => throw new InvalidOperationException("decoder exploded"));

            dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
                new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: @"C:\bad.flac"));

            string warning = await logged.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(@"C:\bad.flac", warning);
            Assert.Contains("decoder exploded", warning);
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.Remove(listener);
        }
    }

    private sealed class WarningCaptureListener : System.Diagnostics.TraceListener
    {
        private readonly TaskCompletionSource<string> _captured;
        public WarningCaptureListener(TaskCompletionSource<string> captured) => _captured = captured;
        public override void Write(string? message) => Capture(message);
        public override void WriteLine(string? message) => Capture(message);
        private void Capture(string? message)
        {
            if (message?.Contains("Background BPM analysis") == true)
                _captured.TrySetResult(message);
        }
    }

    private static Task<PerformanceAction> WaitForDispatch(FakeDispatcher dispatcher, PerformanceActionKind kind)
    {
        var tcs = new TaskCompletionSource<PerformanceAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.ActionDispatched += (_, action) =>
        {
            if (action.Kind == kind)
                tcs.TrySetResult(action);
        };
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            Assert.False(timeout.IsCompleted, "Timed out waiting for the condition.");
            await Task.Delay(10);
        }
    }

    // --- Grid edit (DeckSetFirstBeat) ---

    [Fact]
    public async Task SetGridHere_EmitsDeckSetFirstBeat_WithTheWithinBeatAnchorAtThePlayhead()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 1, dispatcher, FakeWaveformProvider.WithDuration(121));
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 1,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 140.0, Argument: @"C:\b.flac"));
        await gridSet;
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSeek, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.5)); // playhead to mid-track
        dispatcher.Dispatched.Clear();

        await vm.SetGridHereCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSetFirstBeat, action.Kind);
        Assert.Equal(DeckViewModel.GridAnchorAtPlayhead(0.5, 121, 140), action.Value, precision: 6);
        Assert.Equal(1, action.Slot);
    }

    [Fact]
    public async Task GridEdit_BeforeATrackLoads_IsANoOp()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher); // no track → no tempo/duration

        await vm.SetGridHereCommand.Execute().ToTask();

        Assert.Empty(dispatcher.Dispatched); // never push a bogus anchor before a track is loaded
    }

    [Theory]
    [InlineData(0.0, 120, 140, 0.0)]      // playhead at start → anchor 0
    [InlineData(0.5, 120, 120, 0.0)]      // 60 s at 120 BPM (0.5 s/beat) → exactly on a beat → 0
    [InlineData(0.5, 121, 100, 0.5)]      // 60.5 s at 100 BPM (0.6 s/beat) → 0.5 s past the last beat
    public void GridAnchorAtPlayhead_FoldsThePlayheadIntoOneBeat(
        double progress, double duration, double bpm, double expected)
    {
        Assert.Equal(expected, DeckViewModel.GridAnchorAtPlayhead(progress, duration, bpm), precision: 6);
    }

    // --- Manual beatgrid edit toolkit (kick-on-kick) ---

    private static async Task<DeckViewModel> LoadedDeck(FakeDispatcher dispatcher, double bpm = 140.0)
    {
        var vm = new DeckViewModel(slot: 0, dispatcher, FakeWaveformProvider.WithDuration(121));
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: bpm, Argument: @"C:\b.flac"));
        await gridSet;
        dispatcher.Dispatched.Clear();
        return vm;
    }

    [Fact]
    public async Task NudgeGridForward_EmitsDeckSetFirstBeat_ShiftedBackByOneStep()
    {
        var dispatcher = new FakeDispatcher();
        DeckViewModel vm = await LoadedDeck(dispatcher); // first-beat starts at 0, grid bpm 140

        await vm.NudgeGridForwardCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSetFirstBeat, action.Kind);
        Assert.Equal(DeckViewModel.NudgedFirstBeat(0.0, 0.004, 140.0), action.Value, precision: 6);
    }

    [Fact]
    public async Task NudgeGridBack_WrapsBelowZeroIntoTheBeat()
    {
        var dispatcher = new FakeDispatcher();
        DeckViewModel vm = await LoadedDeck(dispatcher);

        await vm.NudgeGridBackCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSetFirstBeat, action.Kind);
        Assert.Equal(60.0 / 140.0 - 0.004, action.Value, precision: 6); // 0 - step, folded into one beat
    }

    [Theory]
    [InlineData(0.5, 70.0)]
    [InlineData(2.0, 280.0)]
    public async Task HalveOrDoubleGridBpm_EmitsDeckSetGridBpm(double factor, double expected)
    {
        var dispatcher = new FakeDispatcher();
        DeckViewModel vm = await LoadedDeck(dispatcher); // grid bpm 140

        ReactiveCommand<Unit, Unit> command = factor < 1 ? vm.HalveGridBpmCommand : vm.DoubleGridBpmCommand;
        await command.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSetGridBpm, action.Kind);
        Assert.Equal(expected, action.Value, precision: 6);
    }

    [Fact]
    public async Task SetDownbeatHere_EmitsDeckSetDownbeat_AtThePlayheadSeconds()
    {
        var dispatcher = new FakeDispatcher();
        DeckViewModel vm = await LoadedDeck(dispatcher);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSeek, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.5)); // playhead to mid-track
        dispatcher.Dispatched.Clear();

        await vm.SetDownbeatHereCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSetDownbeat, action.Kind);
        Assert.Equal(0.5 * 121, action.Value, precision: 6);
    }

    [Fact]
    public async Task GridNudge_BeforeATrackLoads_IsANoOp()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        await vm.NudgeGridForwardCommand.Execute().ToTask();

        Assert.Empty(dispatcher.Dispatched);
    }

    [Theory]
    [InlineData(0.0, 0.004, 140.0, 0.004)]                 // forward from 0
    [InlineData(0.0, -0.004, 140.0, 60.0 / 140.0 - 0.004)] // back from 0 wraps near one beat
    [InlineData(0.30, 0.004, 60.0, 0.304)]                 // mid-beat, no wrap (beat = 1.0 s)
    public void NudgedFirstBeat_FoldsIntoOneBeat(double current, double delta, double bpm, double expected)
    {
        Assert.Equal(expected, DeckViewModel.NudgedFirstBeat(current, delta, bpm), precision: 6);
    }

    // --- Loop-length selector (1/64 … 8 bars) ---

    [Theory]
    [InlineData(1 / 64.0, "1/64")]
    [InlineData(1 / 8.0, "1/8")]
    [InlineData(1 / 2.0, "1/2")]
    [InlineData(1.0, "1")]
    [InlineData(2.0, "2")]
    [InlineData(4.0, "1 BAR")]
    [InlineData(8.0, "2 BAR")]
    [InlineData(32.0, "8 BAR")]
    public void FormatLoopLength_LabelsBeatsAndBars(double beats, string expected)
    {
        Assert.Equal(expected, DeckViewModel.FormatLoopLength(beats));
    }

    [Fact]
    public void LoopLengthKnob_DefaultsToOneBar()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());
        Assert.Equal("1 BAR", vm.LoopLengthLabel); // default 4 beats (index 8 of 12)
    }

    [Theory]
    [InlineData(0.0, "1/64")]   // knob fully left → shortest
    [InlineData(1.0, "8 BAR")]  // fully right → 32 beats
    [InlineData(6.0 / 11, "1")] // index 6 → 1 beat
    public void LoopLengthKnob_MapsPositionToLength(double knob, string expectedLabel)
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher()) { LoopLengthKnob = knob };
        Assert.Equal(expectedLabel, vm.LoopLengthLabel);
    }

    [Theory]
    [InlineData(-0.5, "1/64")] // below range clamps to shortest
    [InlineData(2.0, "8 BAR")] // above range clamps to longest
    public void LoopLengthKnob_ClampsOutOfRange(double knob, string expectedLabel)
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher()) { LoopLengthKnob = knob };
        Assert.Equal(expectedLabel, vm.LoopLengthLabel);
    }

    [Fact]
    public void LoopCommand_ArmsTheKnobSelectedLength()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher) { LoopLengthKnob = 1.0 }; // 32 beats = 8 bars
        dispatcher.Dispatched.Clear();

        vm.LoopCommand.Execute().Subscribe();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckSetLoop, action.Kind);
        Assert.Equal(32.0, action.Value, precision: 6);
    }


    [Fact]
    public async Task BeatGrid_DownbeatBarOffset_TracksDeckSetDownbeatFeedback()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher, FakeWaveformProvider.WithDuration(4));
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120.0, Argument: @"C:\a.flac"));
        await gridSet; // 120 BPM = 0.5 s/beat, first beat at 0

        // SET ONE one beat in (0.5 s) → bar starts on beat index 1 of the grid.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetDownbeat, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.5));

        Assert.Equal(1, vm.DownbeatBarOffset);
    }

    [Fact]
    public async Task Load_AnchorsTheBarOnTheAnalyzedDownbeat_WhenConfident()
    {
        var dispatcher = new FakeDispatcher();
        // 120 BPM, downbeat at 1.0 s (= two beats in), high confidence → trusted.
        var analysis = new Liveolator.Core.Analysis.Bpm.BpmResult(120.0, 0.9, FirstBeatSeconds: 0.0)
        {
            DownbeatSeconds = 1.0,
            DownbeatConfidence = 0.9,
        };
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(4), trackInfo: null, analysisInfo: _ => analysis);
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120.0, Argument: @"C:\a.flac"));
        await gridSet;

        Assert.Equal(2, vm.DownbeatBarOffset); // 1.0 s / 0.5 s-beat = beat 2 of the bar
    }

    [Fact]
    public async Task Load_LeavesTheBarAtTheDefault_WhenTheDownbeatIsLowConfidence()
    {
        var dispatcher = new FakeDispatcher();
        // Same downbeat, but four-on-the-floor low confidence → not trusted; bars stay at index 0.
        var analysis = new Liveolator.Core.Analysis.Bpm.BpmResult(120.0, 0.9, FirstBeatSeconds: 0.0)
        {
            DownbeatSeconds = 1.0,
            DownbeatConfidence = 0.1,
        };
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(4), trackInfo: null, analysisInfo: _ => analysis);
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120.0, Argument: @"C:\a.flac"));
        await gridSet;

        Assert.Equal(0, vm.DownbeatBarOffset);
    }

    [Fact]
    public async Task Load_DispatchesTheConfidentAnalyzedDownbeat_ThroughTheActionSeam()
    {
        var dispatcher = new FakeDispatcher();
        var analysis = new Liveolator.Core.Analysis.Bpm.BpmResult(120.0, 0.9, FirstBeatSeconds: 0.0)
        {
            DownbeatSeconds = 1.0,
            DownbeatConfidence = 0.9,
        };
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(4), trackInfo: null, analysisInfo: _ => analysis);
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120.0, Argument: @"C:\a.flac"));
        await gridSet;

        // The "one" reaches the engine (bar-level sync snap) — not just the local bar-marker display —
        // and is stamped with the analysis origin so persistence never saves it as a manual edit.
        PerformanceAction one = Assert.Single(
            dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetDownbeat);
        Assert.Equal(1.0, one.Value, precision: 6);
        Assert.Equal(0, one.Slot);
        Assert.Equal(DeckViewModel.AnalysisOrigin, one.Origin);
    }

    [Fact]
    public async Task Load_DoesNotDispatchTheDownbeat_WhenLowConfidence()
    {
        var dispatcher = new FakeDispatcher();
        var analysis = new Liveolator.Core.Analysis.Bpm.BpmResult(120.0, 0.9, FirstBeatSeconds: 0.0)
        {
            DownbeatSeconds = 1.0,
            DownbeatConfidence = 0.1,
        };
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(4), trackInfo: null, analysisInfo: _ => analysis);
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120.0, Argument: @"C:\a.flac"));
        await gridSet;

        // An ambiguous bar (four-on-the-floor) must not push a guessed "one" into the engine's bar snap.
        Assert.DoesNotContain(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetDownbeat);
    }

    [Fact]
    public async Task Load_DoesNotOverwriteARestoredManualDownbeat_WithTheAnalyzedOne()
    {
        var dispatcher = new FakeDispatcher();
        // The session restore already re-applied a manual SET ONE for this load (loads reset the engine's
        // anchor to 0, so a non-zero value can only be a restore/manual edit for THIS track).
        dispatcher.SeedFeedback(PerformanceActionKind.DeckSetDownbeat, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.5));
        var analysis = new Liveolator.Core.Analysis.Bpm.BpmResult(120.0, 0.9, FirstBeatSeconds: 0.0)
        {
            DownbeatSeconds = 1.0,
            DownbeatConfidence = 0.9,
        };
        var vm = new DeckViewModel(
            slot: 0, dispatcher, FakeWaveformProvider.WithDuration(4), trackInfo: null, analysisInfo: _ => analysis);
        Task gridSet = WaitForBeatGrid(vm);
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120.0, Argument: @"C:\a.flac"));
        await gridSet;
        // The restore's own feedback echo arrives after the load (DispatchLoad dispatches load THEN downbeat).
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckSetDownbeat, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.5));

        // The DJ's anchor wins: no analyzer dispatch, and the bars sit on the restored 0.5 s (beat 1 of
        // the 120 BPM grid), not the analyzer's 1.0 s (beat 2).
        Assert.DoesNotContain(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetDownbeat);
        Assert.Equal(1, vm.DownbeatBarOffset);
    }

    [Fact]
    public void Constructor_KeepsTheRestoredDownbeat_OverTheAnalyzedOne()
    {
        var dispatcher = new FakeDispatcher();
        // A deck restored before this VM existed: the load AND the manual downbeat are already feedback.
        dispatcher.SeedFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 120.0, Argument: @"C:\a.flac"));
        dispatcher.SeedFeedback(PerformanceActionKind.DeckSetDownbeat, 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0.5));
        var analysis = new Liveolator.Core.Analysis.Bpm.BpmResult(120.0, 0.9, FirstBeatSeconds: 0.0)
        {
            DownbeatSeconds = 1.0,
            DownbeatConfidence = 0.9,
        };

        var vm = new DeckViewModel(slot: 0, dispatcher, trackInfo: null, analysisInfo: _ => analysis);

        // The ctor path must not push the analyzer's guess over the engine's restored anchor.
        Assert.DoesNotContain(dispatcher.Dispatched, a => a.Kind == PerformanceActionKind.DeckSetDownbeat);
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

    // --- BpmFaderValue ---

    [Fact]
    public void BpmFaderValue_ReturnsCentre_WhenNoTrackLoaded()
    {
        var vm = new DeckViewModel(slot: 0, new FakeDispatcher());

        // MinimumBpm == MaximumBpm == 0 → degenerate range → safe centre
        Assert.Equal(0.5, vm.BpmFaderValue, precision: 6);
    }

    [Fact]
    public void BpmFaderValue_NormalizesCurrentBpmInRange()
    {
        var dispatcher = new FakeDispatcher();
        // Simulate feedback: BPM = 120, range = 110.4..129.6
        dispatcher.SeedFeedback(PerformanceActionKind.DeckBpm, slot: 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 120.0,
                Argument: "110.4|129.6"));
        var vm = new DeckViewModel(slot: 0, dispatcher);

        // 120 is the midpoint of 110.4..129.6 → fader ≈ 0.5
        Assert.Equal(0.5, vm.BpmFaderValue, precision: 3);
    }

    [Fact]
    public void BpmFaderValue_Setter_DispatchesDeckBpm_WithDenormalizedValue()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(PerformanceActionKind.DeckBpm, slot: 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 120.0,
                Argument: "110.0|130.0"));
        var vm = new DeckViewModel(slot: 0, dispatcher);
        dispatcher.Dispatched.Clear();

        vm.BpmFaderValue = 1.0; // full right = MaximumBpm = 130

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckBpm, action.Kind);
        Assert.Equal(130.0, action.Value, precision: 3);
    }

    [Fact]
    public void BpmFaderValue_RaisesPropertyChanged_WhenBpmChangesViaFeedback()
    {
        var dispatcher = new FakeDispatcher();
        dispatcher.SeedFeedback(PerformanceActionKind.DeckBpm, slot: 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 120.0,
                Argument: "110.0|130.0"));
        var vm = new DeckViewModel(slot: 0, dispatcher);
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.BpmFaderValue)) raised = true; };

        // Simulate engine feedback: BPM changed
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckBpm, slot: 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 125.0,
                Argument: "110.0|130.0"));

        Assert.True(raised);
    }

    // --- NudgeLeft / NudgeRight ---

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task NudgeLeft_EmitsDeckBpmNudge_WithNegativeDelta(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot, dispatcher);

        await vm.NudgeLeftCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckBpmNudge, action.Kind);
        Assert.Equal(ActionInputMode.Relative, action.InputMode);
        Assert.True(action.Value < 0, "nudge left should carry a negative delta");
        Assert.Equal(slot, action.Slot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task NudgeRight_EmitsDeckBpmNudge_WithPositiveDelta(int slot)
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot, dispatcher);

        await vm.NudgeRightCommand.Execute().ToTask();

        PerformanceAction action = Assert.Single(dispatcher.Dispatched);
        Assert.Equal(PerformanceActionKind.DeckBpmNudge, action.Kind);
        Assert.Equal(ActionInputMode.Relative, action.InputMode);
        Assert.True(action.Value > 0, "nudge right should carry a positive delta");
        Assert.Equal(slot, action.Slot);
    }

    [Fact]
    public async Task NudgeLeft_And_NudgeRight_UseSymmetricStep()
    {
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher);

        await vm.NudgeLeftCommand.Execute().ToTask();
        await vm.NudgeRightCommand.Execute().ToTask();

        double left = dispatcher.Dispatched[0].Value;
        double right = dispatcher.Dispatched[1].Value;
        Assert.Equal(-left, right, precision: 6);
    }

    private sealed class FakeAutoCueService : Liveolator.Core.Analysis.Cues.IAutoCueService
    {
        public List<string> Requested { get; } = new();

        public Task<Liveolator.Core.Analysis.Cues.AutoCueOutcome> RunAsync(
            IReadOnlyList<string> trackPaths,
            IProgress<Liveolator.Core.Analysis.Cues.AutoCueProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requested.AddRange(trackPaths);
            return Task.FromResult(new Liveolator.Core.Analysis.Cues.AutoCueOutcome(trackPaths.Count, trackPaths.Count));
        }
    }

    [Fact]
    public void CanAutoCue_requires_a_wired_service_and_a_loaded_track()
    {
        // No service wired → never available, even with a track loaded.
        Assert.False(new DeckViewModel(slot: 0, new FakeDispatcher()).CanAutoCue);

        // Service wired but nothing loaded → still unavailable (no dead button on an empty deck).
        var dispatcher = new FakeDispatcher();
        var vm = new DeckViewModel(slot: 0, dispatcher, autoCueService: new FakeAutoCueService());
        Assert.False(vm.CanAutoCue);

        // A track loads → AUTO-CUE becomes available.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 128, Argument: @"C:\song.flac"));
        Assert.True(vm.CanAutoCue);
    }

    [Fact]
    public async Task AutoCue_analyzesTheLoadedTrack_thenDispatchesDeckApplyAutoCues()
    {
        var dispatcher = new FakeDispatcher();
        var service = new FakeAutoCueService();
        var vm = new DeckViewModel(slot: 1, dispatcher, autoCueService: service);
        // Simulate the engine echoing the loaded track so the deck knows which file to auto-cue.
        dispatcher.RaiseFeedback(PerformanceActionKind.DeckLoadTrack, 1,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: 0, Argument: @"C:\music\track.mp3"));

        await vm.AutoCueCommand.Execute().ToTask();

        Assert.Equal(@"C:\music\track.mp3", Assert.Single(service.Requested));
        Assert.Contains(dispatcher.Dispatched,
            a => a.Kind == PerformanceActionKind.DeckApplyAutoCues && a.Slot == 1);
    }

    [Fact]
    public void AutoCue_withNoLoadedTrack_cannotExecute()
    {
        // With no track loaded the command is disabled (the button is hidden via CanAutoCue), so it can
        // never run as a silent no-op — the dead-button class the gating eliminates.
        var dispatcher = new FakeDispatcher();
        var service = new FakeAutoCueService();
        var vm = new DeckViewModel(slot: 0, dispatcher, autoCueService: service);

        bool canExecute = true;
        using (vm.AutoCueCommand.CanExecute.Subscribe(v => canExecute = v)) { }

        Assert.False(canExecute);
        Assert.Empty(service.Requested);
    }
}
