using System;
using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Mixer;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

public class TwoDeckBassEngineTests
{
    /// <summary>Fixed host clock — the beat clock derives BPM from frame timestamps, not host time.</summary>
    private sealed class FixedHostClock : IHostClock
    {
        public long TicksPerSecond => 1_000_000;
        public long NowTicks => 0;
    }

    private static TwoDeckBassEngine NewEngine(out FakeBassMixerBackend backend, out BassMixer mixer)
    {
        backend = new FakeBassMixerBackend();
        mixer = new BassMixer(deckCount: TwoDeckBassEngine.Decks);
        return new TwoDeckBassEngine(backend, mixer);
    }

    [Fact]
    public void DeckCount_IsFour()
    {
        // 2 live decks (A/B) + 2 hidden STUDIO decks (C/D).
        using var engine = NewEngine(out _, out _);
        Assert.Equal(4, engine.DeckCount);
    }

    [Fact]
    public void Load_WhenTheNewTrackFailsToOpen_KeepsThePreviousTrackLoadedAndPlayable()
    {
        // Regression: a stale live-queue / restored entry pointing at a missing file must NOT wipe a deck
        // that already holds a good, playable track (the "Deck A shows a track but won't play" bug).
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\good.wav"); // opens handle 100, plugged into slot 0

        backend.OpenOverride = _ => throw new InvalidOperationException("file not found");
        Assert.Throws<InvalidOperationException>(() => engine.Load(0, @"C:\missing.wav"));

        // The good track is still loaded (never unplugged) and the deck still plays.
        Assert.DoesNotContain(100, backend.Unplugged);
        engine.PlayPause(0);
        Assert.True(engine.IsPlaying(0));
    }

    [Fact]
    public void Ctor_ArmsMasterTapOnce()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        Assert.Equal(1, backend.MasterStarts);
    }

    [Fact]
    public void MixerTooSmall_Throws()
    {
        var backend = new FakeBassMixerBackend();
        Assert.Throws<ArgumentException>(() => new TwoDeckBassEngine(backend, new BassMixer(deckCount: 1)));
    }

    [Fact]
    public void ReinitializeOutput_ForwardsResolvedInitOptionsToBackend()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);

        bool ok = engine.ReinitializeOutput(new AudioSettings { OutputDeviceId = "3", BufferMilliseconds = 80 });

        Assert.True(ok);
        BassInitOptions applied = Assert.Single(backend.Reinits);
        Assert.Equal(3, applied.DeviceIndex);
        Assert.Equal(80, applied.BufferMilliseconds);
    }

    [Fact]
    public void ReinitializeOutput_BackendFailure_ReturnsFalse()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        backend.ReinitResult = false;

        Assert.False(engine.ReinitializeOutput(AudioSettings.Default));
    }

    [Fact]
    public void Load_OpensStreamAndRegistersChannelIntoMixer()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out BassMixer mixer);

        engine.Load(1, @"C:\b.wav");

        Assert.Contains(@"C:\b.wav", backend.Opened);
        // The channel plugged for slot 1 is now the one BassMixer routes to — prove the missing seam.
        mixer.SetDeckGain(1, 0.5);
        Assert.Equal(0.5, backend.Channels[100].Volume);
    }

    [Fact]
    public void MixerActions_RouteToTheLoadedDeckChannel_EndToEnd()
    {
        // The Core handler computes the math; the engine registered the channel; BASS_FX gets it.
        using var engine = NewEngine(out FakeBassMixerBackend backend, out BassMixer mixer);
        engine.Load(0, @"C:\a.wav");
        var handler = new MixerActionHandler(mixer);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, Value: -1.0)); // full deck A

        Assert.Equal(1.0, backend.Channels[100].Volume!.Value, 6);
    }

    [Fact]
    public void Load_Twice_UnplugsPreviousDeckAndClearsItsChannel()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out BassMixer mixer);

        engine.Load(0, @"C:\a.wav"); // handle 100
        engine.Load(0, @"C:\b.wav"); // handle 101

        Assert.Contains(100, backend.Unplugged);
        // After replacement, slot 0 routes to the new channel (handle 101), not the old one.
        // Capture old channel's volume before the explicit gain update: SetChannel already applied
        // the default unity gain (1.0) on initial load, so it will be non-null here.
        double? oldVolumeBeforeGainUpdate = backend.Channels[100].Volume;
        mixer.SetDeckGain(0, 0.25);
        // Old channel must not have received the 0.25 update — its stored volume is unchanged.
        Assert.Equal(oldVolumeBeforeGainUpdate, backend.Channels[100].Volume);
        Assert.Equal(0.25, backend.Channels[101].Volume);
    }

    [Fact]
    public void PlayPause_TogglesIsPlaying()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");

        Assert.False(engine.IsPlaying(0));
        engine.PlayPause(0);
        Assert.True(engine.IsPlaying(0));
        engine.PlayPause(0);
        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void PlayPause_IsPerSlot()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav");

        engine.PlayPause(0);

        Assert.True(engine.IsPlaying(0));
        Assert.False(engine.IsPlaying(1));
    }

    [Fact]
    public void PlayPause_NothingLoaded_IsNoOp()
    {
        using var engine = NewEngine(out _, out _);

        engine.PlayPause(0);

        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void Stop_StopsDeck()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");
        engine.PlayPause(0);

        engine.Stop(0);

        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void Load_EmptyPath_Throws()
    {
        using var engine = NewEngine(out _, out _);
        Assert.Throws<ArgumentException>(() => engine.Load(0, "  "));
    }

    [Fact]
    public void OutOfRangeSlot_Throws()
    {
        using var engine = NewEngine(out _, out _);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Load(2, @"C:\a.wav"));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.PlayPause(-1));
    }

    [Fact]
    public void Dispose_UnplugsAllDecksAndDisposesBackend()
    {
        var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav");

        engine.Dispose();

        Assert.Contains(100, backend.Unplugged);
        Assert.Contains(101, backend.Unplugged);
        Assert.True(backend.Disposed);
    }

    // --- Transport: seek / pitch / cue / sync / quantize ---

    [Fact]
    public void Seek_Absolute_SetsBackendPositionFraction()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100

        engine.Seek(0, 0.4, relative: false);

        Assert.Equal(0.4, backend.PositionFraction[100], 6);
    }

    [Fact]
    public void Seek_Relative_AddsToCurrentAndClamps()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        backend.PositionFraction[100] = 0.9;

        engine.Seek(0, 0.5, relative: true); // 0.9 + 0.5 -> clamp 1.0

        Assert.Equal(1.0, backend.PositionFraction[100], 6);
    }

    [Fact]
    public void Seek_NothingLoaded_IsNoOp()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);

        engine.Seek(0, 0.5, relative: false);

        Assert.Empty(backend.PositionFraction);
    }

    [Fact]
    public void Jog_MovesBySecondsIndependentOfTrackLength()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        backend.LengthSeconds[100] = 300.0;
        backend.PositionFraction[100] = 0.5;

        engine.Jog(0, 1.8);

        Assert.Equal(151.8 / 300.0, backend.PositionFraction[100], 6);
    }

    [Theory]
    [InlineData(0.01, -5.0, 0.0)]
    [InlineData(0.99, 5.0, 1.0)]
    public void Jog_ClampsAtTrackBoundaries(double start, double deltaSeconds, double expected)
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(1, @"C:\b.wav");
        backend.PositionFraction[100] = start;

        engine.Jog(1, deltaSeconds);

        Assert.Equal(expected, backend.PositionFraction[100], 6);
    }

    [Fact]
    public void Pitch_Center_IsOriginalRate_AndEndsMapToPlusMinus8Percent()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100, seeded at centre on load

        Assert.Equal(1.0, backend.Rate[100], 6); // centre applied at load

        engine.SetPitch(0, 1.0, relative: false);
        Assert.Equal(1.08, backend.Rate[100], 6);

        engine.SetPitch(0, 0.0, relative: false);
        Assert.Equal(0.92, backend.Rate[100], 6);

        Assert.Equal(0.0, engine.PitchPosition(0), 6);
    }

    [Fact]
    public void Pitch_PersistsAcrossLoad_AndReappliesToNewTrack()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");          // handle 100
        engine.SetPitch(0, 1.0, relative: false); // top of range

        engine.Load(0, @"C:\b.wav");          // handle 101 — pitch fader stays put

        Assert.Equal(1.0, engine.PitchPosition(0), 6);
        Assert.Equal(1.08, backend.Rate[101], 6);
    }

    [Fact]
    public void Cue_JumpsToStartAndPauses()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100
        engine.PlayPause(0);
        backend.PositionFraction[100] = 0.7;

        engine.Cue(0);

        Assert.Equal(0.0, backend.PositionFraction[100], 6);
        Assert.False(engine.IsPlaying(0));
    }

    // --- Settable temporary cue (A5: CDJ back-to-cue) ---

    [Fact]
    public void Cue_WhenPausedAwayFromCue_SetsTempCueHere_StaysAtPosition()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100, paused after load
        backend.PositionFraction[100] = 0.42;

        engine.Cue(0); // first press at a fresh spot -> set the temp cue here

        Assert.Equal(0.42, backend.PositionFraction[100], 6); // not moved to start
        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void Cue_AfterSetting_ReturnsToTheSetCue_NotTrackStart()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100
        backend.PositionFraction[100] = 0.42;
        engine.Cue(0); // set temp cue at 0.42

        backend.PositionFraction[100] = 0.8; // playhead moved on
        engine.PlayPause(0);                  // now playing
        engine.Cue(0);                        // back-to-cue -> jump to 0.42, pause

        Assert.Equal(0.42, backend.PositionFraction[100], 6);
        Assert.False(engine.IsPlaying(0));
    }

    // --- End-of-track handling (A4: deck end -> DeckEnded event) ---

    [Fact]
    public void DeckEnd_RaisesDeckEnded_AndMarksSlotStopped()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100
        engine.PlayPause(0);
        Assert.True(engine.IsPlaying(0));

        int? endedSlot = null;
        engine.DeckEnded += (_, slot) => endedSlot = slot;

        backend.EmitDeckEnd(100); // the stream ran out

        Assert.Equal(0, endedSlot);
        Assert.False(engine.IsPlaying(0));
    }

    [Fact]
    public void DeckEnd_OfReplacedDeck_IsIgnored()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100
        engine.Load(0, @"C:\b.wav"); // handle 101 replaces it

        int raises = 0;
        engine.DeckEnded += (_, _) => raises++;

        backend.EmitDeckEnd(100); // a stale end from the replaced stream

        Assert.Equal(0, raises); // ignored — slot now holds handle 101
    }

    [Fact]
    public void DeckEnd_IsPerSlot()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100
        engine.Load(1, @"C:\b.wav"); // handle 101

        var endedSlots = new System.Collections.Generic.List<int>();
        engine.DeckEnded += (_, slot) => endedSlots.Add(slot);

        backend.EmitDeckEnd(101);

        Assert.Equal(new[] { 1 }, endedSlots);
    }

    [Fact]
    public void Cue_TempCueClearedOnReload()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        backend.PositionFraction[100] = 0.42;
        engine.Cue(0); // set cue at 0.42 on track a

        engine.Load(0, @"C:\b.wav"); // handle 101 — fresh track, no temp cue
        engine.PlayPause(0);
        backend.PositionFraction[101] = 0.6;
        engine.Cue(0); // back-to-cue with no set cue -> track start

        Assert.Equal(0.0, backend.PositionFraction[101], 6);
    }

    [Fact]
    public void SyncLock_And_Quantize_AreStoredPerSlot()
    {
        using var engine = NewEngine(out _, out _);

        engine.SetSyncLock(1, true);
        engine.SetQuantize(0, true);

        Assert.True(engine.IsSyncLocked(1));
        Assert.False(engine.IsSyncLocked(0));
        Assert.True(engine.IsQuantizeEnabled(0));
        Assert.False(engine.IsQuantizeEnabled(1));
    }

    // --- Sync Lock: tempo match (beatmatch by BPM, doc 11) ---

    [Fact]
    public void SetDeckBaseBpm_IsStoredPerSlot()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");

        engine.SetDeckBaseBpm(0, 128.0);

        Assert.Equal(128.0, engine.DeckBaseBpm(0), 6);
        Assert.Equal(0.0, engine.DeckBaseBpm(1), 6);
    }

    [Fact]
    public void SetDeckBpm_UpdatesPitchRate_AndClampsToDeckRange()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.SetDeckBaseBpm(0, 120.0);

        engine.SetDeckBpm(0, 126.0);

        Assert.Equal(126.0, engine.DeckBpm(0), 6);
        Assert.Equal(1.05, backend.Rate[100], 6);

        engine.SetDeckBpm(0, 150.0);

        Assert.Equal(129.6, engine.DeckBpm(0), 6);
        Assert.Equal(1.08, backend.Rate[100], 6);
        Assert.Equal(110.4, engine.MinimumDeckBpm(0), 6);
        Assert.Equal(129.6, engine.MaximumDeckBpm(0), 6);
    }

    [Fact]
    public void Sync_MatchesFollowerRateToLeaderBpm()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // leader, handle 100
        engine.Load(1, @"C:\b.wav"); // follower, handle 101
        engine.SetDeckBaseBpm(0, 128.0);
        engine.SetDeckBaseBpm(1, 124.0);

        engine.SetSyncLock(1, true);

        Assert.Equal(128.0 / 124.0, backend.Rate[101], 6);
    }

    [Fact]
    public void Sync_FoldsHalfTempoFollowerToNearUnity()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // 140 BPM leader
        engine.Load(1, @"C:\b.wav"); // 70 BPM follower
        engine.SetDeckBaseBpm(0, 140.0);
        engine.SetDeckBaseBpm(1, 70.0);

        engine.SetSyncLock(1, true);

        Assert.Equal(1.0, backend.Rate[101], 6); // plays at its own 70, aligning every other leader beat
    }

    [Fact]
    public void Sync_Release_RevertsToManualPitchRate()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav");
        engine.SetDeckBaseBpm(0, 128.0);
        engine.SetDeckBaseBpm(1, 124.0);
        engine.SetPitch(1, 1.0, relative: false); // follower pitch fader at +8%

        engine.SetSyncLock(1, true);
        Assert.Equal(128.0 / 124.0, backend.Rate[101], 6); // sync owns the rate

        engine.SetSyncLock(1, false);
        Assert.Equal(1.08, backend.Rate[101], 6); // handed back to the pitch fader
    }

    [Fact]
    public void Sync_NoLeaderBpm_LeavesRateUnchanged()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(1, @"C:\b.wav"); // only the follower is loaded (handle 100)
        engine.SetDeckBaseBpm(1, 124.0);

        engine.SetSyncLock(1, true); // leader slot 0 is empty

        Assert.Equal(1.0, backend.Rate[100], 6); // still the load-time centre rate, no throw
    }

    [Fact]
    public void Sync_LeaderBaseBpmChange_ReappliesFollowerRate()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav");
        engine.SetDeckBaseBpm(0, 128.0);
        engine.SetDeckBaseBpm(1, 124.0);
        engine.SetSyncLock(1, true);

        engine.SetDeckBaseBpm(0, 140.0); // leader retuned

        Assert.Equal(140.0 / 124.0, backend.Rate[101], 6);
    }

    [Fact]
    public void Sync_FollowsLeaderPitchFader()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav");
        engine.SetDeckBaseBpm(0, 124.0);
        engine.SetDeckBaseBpm(1, 124.0);
        engine.SetSyncLock(1, true);
        Assert.Equal(1.0, backend.Rate[101], 6); // equal tempos

        engine.SetPitch(0, 1.0, relative: false); // nudge the leader's pitch fader to +8%

        Assert.Equal(1.08, backend.Rate[101], 6); // follower tracks the leader's audible tempo
    }

    [Fact]
    public void Position_ReadsBackend_ZeroWhenNothingLoaded()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        Assert.Equal(0.0, engine.Position(0), 6);

        engine.Load(0, @"C:\a.wav");
        backend.PositionFraction[100] = 0.33;
        Assert.Equal(0.33, engine.Position(0), 6);
    }

    [Fact]
    public void HotCue_FirstPressSetsAtCurrentPosition_SecondPressJumpsToIt()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // handle 100
        backend.PositionFraction[100] = 0.42;

        engine.HotCue(0, 2);                 // set cue 2 at 0.42
        Assert.True(engine.IsHotCueSet(0, 2));

        backend.PositionFraction[100] = 0.9; // playhead moved on
        engine.HotCue(0, 2);                 // jump back to 0.42

        Assert.Equal(0.42, backend.PositionFraction[100], 6);
    }

    [Fact]
    public void HotCue_IsPerSlotAndPerIndex()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.HotCue(0, 1);

        Assert.True(engine.IsHotCueSet(0, 1));
        Assert.False(engine.IsHotCueSet(0, 2));
        Assert.False(engine.IsHotCueSet(1, 1));
    }

    [Fact]
    public void HotCue_ClearedWhenTrackReloads()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");
        engine.HotCue(0, 0);
        Assert.True(engine.IsHotCueSet(0, 0));

        engine.Load(0, @"C:\b.wav");

        Assert.False(engine.IsHotCueSet(0, 0));
    }

    [Fact]
    public void HotCue_NothingLoaded_IsNoOp()
    {
        using var engine = NewEngine(out _, out _);

        engine.HotCue(0, 0);

        Assert.False(engine.IsHotCueSet(0, 0));
    }

    [Fact]
    public void HotCue_IndexOutOfRange_Throws()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.HotCue(0, engine.HotCueCount));
    }

    // --- Persistent hot cues (A3): store wired into the engine, load on Load, save on set ---

    private static TwoDeckBassEngine NewEngineWithStore(
        out FakeBassMixerBackend backend, out FakeHotCueStore store)
    {
        backend = new FakeBassMixerBackend();
        store = new FakeHotCueStore();
        return new TwoDeckBassEngine(backend, new BassMixer(deckCount: TwoDeckBassEngine.Decks), hotCueStore: store);
    }

    [Fact]
    public void HotCue_SettingACue_PersistsToTheStore()
    {
        using var engine = NewEngineWithStore(out FakeBassMixerBackend backend, out FakeHotCueStore store);
        engine.Load(0, @"C:\a.wav"); // handle 100, length defaults to 100 s, master rate 48 kHz
        backend.PositionFraction[100] = 0.5;

        engine.HotCue(0, 1); // set cue 1 at 0.5

        Assert.True(store.SaveCount >= 1);
        TrackCueRecord? saved = store.Get(@"C:\a.wav");
        Assert.NotNull(saved);
        // 0.5 of 100 s at 48 kHz = 2_400_000 samples.
        Assert.Equal(2_400_000L, saved!.HotCues[0].PositionSamples);
    }

    [Fact]
    public void Load_RestoresPersistedCues_AsFractions()
    {
        using var engine = NewEngineWithStore(out FakeBassMixerBackend backend, out FakeHotCueStore store);
        // Seed a cue at 2_400_000 samples (= 0.5 of a 100 s/48 kHz track) on slot index 3.
        var set = new TrackCueSet(48_000, slotCount: 8).SetHotCue(3, 2_400_000);
        store.Seed(TrackCueRecord.FromCueSet(@"C:\a.wav", set));

        engine.Load(0, @"C:\a.wav"); // handle 100

        Assert.True(engine.IsHotCueSet(0, 3));
        // A second press should jump the playhead to the restored fraction (0.5).
        backend.PositionFraction[100] = 0.9;
        engine.HotCue(0, 3);
        Assert.Equal(0.5, backend.PositionFraction[100], precision: 6);
    }

    [Fact]
    public void Load_DifferentTracks_RestoreTheirOwnCues()
    {
        using var engine = NewEngineWithStore(out _, out FakeHotCueStore store);
        store.Seed(TrackCueRecord.FromCueSet(@"C:\a.wav", new TrackCueSet(48_000, 8).SetHotCue(0, 480_000)));
        // b.wav has no saved cues.

        engine.Load(0, @"C:\a.wav");
        Assert.True(engine.IsHotCueSet(0, 0));

        engine.Load(0, @"C:\b.wav"); // replaces; b has no cues
        Assert.False(engine.IsHotCueSet(0, 0));
    }

    [Fact]
    public void Load_StoreThrows_DegradesToNoCues_DoesNotThrow()
    {
        using var engine = NewEngineWithStore(out _, out FakeHotCueStore store);
        store.Throw = true;

        engine.Load(0, @"C:\a.wav"); // must not bubble the store failure

        Assert.False(engine.IsHotCueSet(0, 0));
    }

    [Fact]
    public void HotCue_SaveThrows_IsSwallowed()
    {
        using var engine = NewEngineWithStore(out FakeBassMixerBackend backend, out FakeHotCueStore store);
        engine.Load(0, @"C:\a.wav");
        store.Throw = true;
        backend.PositionFraction[100] = 0.4;

        // Setting a cue while the store rejects the save must not crash the show.
        engine.HotCue(0, 0);

        Assert.True(engine.IsHotCueSet(0, 0)); // the in-RAM cue still latched
    }

    // --- Loops (beat-length -> time region via base BPM, doc 11) ---

    [Fact]
    public void SetLoop_ConvertsBeatLengthToTimeRegionUsingBaseBpm()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");          // handle 100
        engine.SetDeckBaseBpm(0, 120.0);      // 0.5 s/beat
        backend.PositionFraction[100] = 0.1;  // start at 10 s (length defaults to 100 s in the fake)

        engine.SetLoop(0, 4.0);               // 4 beats -> 2 s region

        (double start, double end) = backend.Loops[100];
        Assert.Equal(10.0, start, precision: 6);
        Assert.Equal(12.0, end, precision: 6);
        Assert.True(engine.IsLooping(0));
        Assert.Equal(4.0, engine.LoopBeats(0), precision: 6);
    }

    [Fact]
    public void SetLoop_WithQuantizeArmed_SnapsTheInPointToTheBeatGrid()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");          // handle 100, length 100 s, first beat 0
        engine.SetDeckBaseBpm(0, 120.0);      // 0.5 s/beat
        engine.SetQuantize(0, true);
        backend.PositionFraction[100] = 0.104; // set after arming Quantize: playhead at 10.4 s, off-grid

        engine.SetLoop(0, 4.0);               // 4 beats -> 2 s region, snapped start

        (double start, double end) = backend.Loops[100];
        Assert.Equal(10.5, start, precision: 6); // snapped to the nearest beat (21 * 0.5 s), not the raw 10.4
        Assert.Equal(12.5, end, precision: 6);
    }

    [Fact]
    public void SetLoop_WithoutQuantize_StartsAtTheRawPlayhead()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.SetDeckBaseBpm(0, 120.0);
        backend.PositionFraction[100] = 0.104; // 10.4 s, off-grid; Quantize off (default)

        engine.SetLoop(0, 4.0);

        Assert.Equal(10.4, backend.Loops[100].Start, precision: 6); // unchanged — no grid snap
    }

    [Fact]
    public void SetLoop_UnknownBaseBpm_IsIgnored()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // no base BPM set

        engine.SetLoop(0, 4.0);

        Assert.False(engine.IsLooping(0));
        Assert.DoesNotContain(100, backend.Loops.Keys);
    }

    [Fact]
    public void SetLoop_NothingLoaded_IsNoOp()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.SetDeckBaseBpm(0, 120.0);

        engine.SetLoop(0, 4.0);

        Assert.False(engine.IsLooping(0));
        Assert.Empty(backend.Loops);
    }

    [Fact]
    public void ClearLoop_RemovesTheActiveLoop()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.SetDeckBaseBpm(0, 120.0);
        engine.SetLoop(0, 4.0);

        engine.ClearLoop(0);

        Assert.False(engine.IsLooping(0));
        Assert.DoesNotContain(100, backend.Loops.Keys);
        Assert.Contains(100, backend.LoopsCleared);
    }

    [Fact]
    public void SetLoop_ClearedWhenTrackReloads()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");
        engine.SetDeckBaseBpm(0, 120.0);
        engine.SetLoop(0, 4.0);
        Assert.True(engine.IsLooping(0));

        engine.Load(0, @"C:\b.wav");

        Assert.False(engine.IsLooping(0));
    }

    [Fact]
    public void LoopBeats_ScalesRegionWithBpm()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.SetDeckBaseBpm(0, 160.0); // faster tempo -> shorter region
        backend.PositionFraction[100] = 0.0;

        engine.SetLoop(0, 4.0); // 4 beats * (60/160) = 1.5 s

        (double start, double end) = backend.Loops[100];
        Assert.Equal(1.5, end - start, precision: 6);
    }

    // --- Phase match (Quantize aligns the deck playhead to the leader grid, doc 11) ---

    [Fact]
    public void Quantize_SnapsFollowerPlayheadToLeaderBeatPhase()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav"); // leader, handle 100
        engine.Load(1, @"C:\b.wav"); // follower, handle 101
        engine.SetDeckBaseBpm(0, 120.0);
        engine.SetDeckBaseBpm(1, 120.0);
        engine.SetDeckFirstBeat(0, 0.0);
        engine.SetDeckFirstBeat(1, 0.0);
        // Length defaults to 100 s. Leader at 0.25 s (half a 120-BPM beat into its grid); follower on a
        // beat (0 s). Phase-match should advance the follower +0.25 s -> fraction 0.0025.
        backend.PositionFraction[100] = 0.0025; // 0.25 s
        backend.PositionFraction[101] = 0.0;    // 0 s

        engine.SetQuantize(1, true);

        Assert.True(engine.IsQuantizeEnabled(1));
        Assert.Equal(0.0025, backend.PositionFraction[101], precision: 6); // 0.25 s / 100 s
    }

    [Fact]
    public void Quantize_NoLeader_LeavesPlayheadUnchanged()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(1, @"C:\b.wav"); // only the follower loaded (handle 100)
        engine.SetDeckBaseBpm(1, 120.0);
        engine.SetDeckFirstBeat(1, 0.0);
        backend.PositionFraction[100] = 0.3;

        engine.SetQuantize(1, true);

        Assert.True(engine.IsQuantizeEnabled(1)); // armed
        Assert.Equal(0.3, backend.PositionFraction[100], precision: 6); // but no guess
    }

    [Fact]
    public void Quantize_OwnAnchorUnknownBpm_LeavesPlayheadUnchanged()
    {
        using var engine = NewEngine(out FakeBassMixerBackend backend, out _);
        engine.Load(0, @"C:\a.wav");
        engine.Load(1, @"C:\b.wav"); // handle 101, no base BPM -> own tempo unknown
        engine.SetDeckBaseBpm(0, 120.0);
        backend.PositionFraction[101] = 0.4;

        engine.SetQuantize(1, true);

        Assert.Equal(0.4, backend.PositionFraction[101], precision: 6);
    }

    [Fact]
    public void SetDeckFirstBeat_IsStoredPerSlot()
    {
        using var engine = NewEngine(out _, out _);

        engine.SetDeckFirstBeat(0, 0.08);

        Assert.Equal(0.08, engine.DeckFirstBeat(0), precision: 6);
        Assert.Equal(0.0, engine.DeckFirstBeat(1), precision: 6);
    }

    [Fact]
    public void SetDeckFirstBeat_ClearedOnReload()
    {
        using var engine = NewEngine(out _, out _);
        engine.Load(0, @"C:\a.wav");
        engine.SetDeckFirstBeat(0, 0.08);

        engine.Load(0, @"C:\b.wav");

        Assert.Equal(0.0, engine.DeckFirstBeat(0), precision: 6);
    }

    [Fact]
    public void MasterMix_FeedsBeatClock_EndToEnd()
    {
        // The spine of the increment: the master tap -> MasterMixPlaybackEngine -> beat clock.
        // A 120 BPM click pushed through the master is detected by the live clock.
        const int rate = 44_100;
        const int period = 22_050;   // impulse every 0.5 s -> 120 BPM
        const int seconds = 12;
        int total = rate * seconds;

        // The master format is read in the engine ctor, so set the rate before constructing it.
        var backend = new FakeBassMixerBackend { MasterInfo = new MasterMixInfo(Channels: 2, SampleRate: rate) };
        using var engine = new TwoDeckBassEngine(backend, new BassMixer(deckCount: TwoDeckBassEngine.Decks));
        using var playback = new MasterMixPlaybackEngine(engine.MasterSource, new FixedHostClock());

        const int chunk = rate / 10;
        var buffer = new float[chunk * 2]; // stereo interleaved
        for (int start = 0; start < total; start += chunk)
        {
            Array.Clear(buffer);
            for (int i = 0; i < chunk; i++)
            {
                if ((start + i) % period == 0)
                {
                    buffer[(i * 2)] = 1.0f;     // L
                    buffer[(i * 2) + 1] = 1.0f; // R
                }
            }
            backend.EmitMaster((float[])buffer.Clone());
        }

        Assert.InRange(playback.BeatClock.Current.Bpm, 105.0, 135.0);
    }
}
