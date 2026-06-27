using System;
using System.Collections.Generic;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class DeckActionHandlerTests
{
    private sealed class FakePlaybackEngine : IAudioPlaybackEngine
    {
        public List<string> Loaded { get; } = new();
        public int PlayPauseCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool IsPlaying { get; set; }

        public void Load(string trackPath) => Loaded.Add(trackPath);
        public void PlayPause() => PlayPauseCalls++;
        public void Stop() => StopCalls++;
    }

    [Fact]
    public void HandledKinds_AreLoadPlayPauseAndStop()
    {
        var handler = new DeckActionHandler(new FakePlaybackEngine());

        Assert.Contains(PerformanceActionKind.DeckLoadTrack, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckPlayPause, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.TransportStop, handler.HandledKinds);
    }

    [Fact]
    public void LoadTrack_PassesArgumentPathToEngine()
    {
        var engine = new FakePlaybackEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckLoadTrack, Argument: @"C:\song.flac"));

        Assert.Equal(@"C:\song.flac", Assert.Single(engine.Loaded));
    }

    [Fact]
    public void LoadTrack_WithoutArgument_Throws()
    {
        var handler = new DeckActionHandler(new FakePlaybackEngine());

        Assert.Throws<ArgumentException>(
            () => handler.Handle(new PerformanceAction(PerformanceActionKind.DeckLoadTrack)));
    }

    [Fact]
    public void PlayPauseAndStop_RouteToEngine()
    {
        var engine = new FakePlaybackEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckPlayPause));
        handler.Handle(new PerformanceAction(PerformanceActionKind.TransportStop));

        Assert.Equal(1, engine.PlayPauseCalls);
        Assert.Equal(1, engine.StopCalls);
    }

    [Fact]
    public void Feedback_ReflectsPlayState()
    {
        var engine = new FakePlaybackEngine { IsPlaying = true };
        var handler = new DeckActionHandler(engine);

        ActionFeedbackState fb = handler.GetFeedback(PerformanceActionKind.DeckPlayPause, slot: 0);

        Assert.True(fb.IsActive);
        Assert.True(fb.IsAvailable);
    }

    [Fact]
    public void RoutesThroughDispatcher_EndToEnd()
    {
        var engine = new FakePlaybackEngine();
        var dispatcher = new PerformanceActionDispatcher(
            new[] { new DeckActionHandler(engine) },
            NullLogger<PerformanceActionDispatcher>.Instance);

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckLoadTrack, Argument: @"C:\x.wav"));
        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause));

        Assert.Single(engine.Loaded);
        Assert.Equal(1, engine.PlayPauseCalls);
    }

    // --- Two-deck path (slot-addressed) ---

    private sealed class FakeMultiDeckEngine : IMultiDeckPlaybackEngine
    {
        private readonly bool[] _playing;
        private readonly bool[] _sync;
        private readonly bool[] _quantize;
        private readonly bool[] _keyLock;
        private readonly double[] _position;
        private readonly double[] _pitch;
        private readonly double[] _baseBpm;
        private readonly double[] _bpm;
        private readonly double[] _firstBeat;
        private readonly double[] _loopBeats;

        public List<(int Slot, string Path)> Loaded { get; } = new();
        public List<int> PlayPaused { get; } = new();
        public List<int> Stopped { get; } = new();
        public List<(int Slot, double Position, bool Relative)> Seeks { get; } = new();
        public List<(int Slot, double DeltaSeconds)> Jogs { get; } = new();
        public List<(int Slot, double Value, bool Relative)> Pitches { get; } = new();
        public List<(int Slot, double Bpm)> Bpms { get; } = new();
        public List<(int Slot, double Bend)> Bends { get; } = new();
        public List<int> SyncOnceCalls { get; } = new();
        public List<int> Cues { get; } = new();
        public List<(int Slot, double Beats)> Loops { get; } = new();
        public List<int> LoopsCleared { get; } = new();

        public FakeMultiDeckEngine(int deckCount = 2)
        {
            _playing = new bool[deckCount];
            _sync = new bool[deckCount];
            _quantize = new bool[deckCount];
            _keyLock = new bool[deckCount];
            _position = new double[deckCount];
            _pitch = new double[deckCount];
            _baseBpm = new double[deckCount];
            _bpm = new double[deckCount];
            _firstBeat = new double[deckCount];
            _loopBeats = new double[deckCount];
            for (int i = 0; i < deckCount; i++)
                _pitch[i] = 0.5; // center = original tempo
        }

        public int DeckCount => _playing.Length;
        public event EventHandler<int>? DeckEnded { add { } remove { } }
        public bool IsPlaying(int slot) => _playing[slot];
        public void SetPlaying(int slot, bool value) => _playing[slot] = value;

        /// <summary>When true, <see cref="Load"/> throws — simulates a missing/offline file or the native
        /// audio engine failing to create the deck stream (the real bass_fx-missing failure).</summary>
        public bool ThrowOnLoad { get; set; }
        public void Load(int slot, string trackPath)
        {
            if (ThrowOnLoad)
                throw new InvalidOperationException("native audio engine could not load the track");
            Loaded.Add((slot, trackPath));
        }
        public void PlayPause(int slot) => PlayPaused.Add(slot);
        public void Stop(int slot) => Stopped.Add(slot);

        public double Position(int slot) => _position[slot];
        public double LengthSeconds(int slot) => 0;
        public void Seek(int slot, double position, bool relative)
        {
            Seeks.Add((slot, position, relative));
            _position[slot] = relative ? Math.Clamp(_position[slot] + position, 0, 1) : Math.Clamp(position, 0, 1);
        }

        public void Jog(int slot, double deltaSeconds)
        {
            Jogs.Add((slot, deltaSeconds));
            _position[slot] = Math.Clamp(_position[slot] + deltaSeconds / 100.0, 0, 1);
        }

        public double PitchPosition(int slot) => _pitch[slot];
        public void SetPitch(int slot, double value, bool relative)
        {
            Pitches.Add((slot, value, relative));
            _pitch[slot] = relative ? Math.Clamp(_pitch[slot] + value, 0, 1) : Math.Clamp(value, 0, 1);
        }

        public double DeckBpm(int slot) => _bpm[slot];
        public double MinimumDeckBpm(int slot) => _baseBpm[slot] * 0.92;
        public double MaximumDeckBpm(int slot) => _baseBpm[slot] * 1.08;
        public void SetDeckBpm(int slot, double bpm)
        {
            Bpms.Add((slot, bpm));
            _bpm[slot] = Math.Clamp(bpm, MinimumDeckBpm(slot), MaximumDeckBpm(slot));
        }

        public void PitchBend(int slot, double bendFraction) => Bends.Add((slot, bendFraction));

        public void Cue(int slot)
        {
            Cues.Add(slot);
            _position[slot] = 0;
        }

        public List<(int Slot, bool Pressed)> CuePlays { get; } = new();
        public void CuePlay(int slot, bool isPressed) => CuePlays.Add((slot, isPressed));

        public double DeckBaseBpm(int slot) => _baseBpm[slot];
        public void SetDeckBaseBpm(int slot, double bpm)
        {
            _baseBpm[slot] = bpm;
            _bpm[slot] = bpm;
        }

        public double DeckFirstBeat(int slot) => _firstBeat[slot];
        public void SetDeckFirstBeat(int slot, double firstBeatSeconds) => _firstBeat[slot] = firstBeatSeconds;
        public void SyncOnce(int slot) => SyncOnceCalls.Add(slot);

        public bool IsSyncLocked(int slot) => _sync[slot];
        public void SetSyncLock(int slot, bool enabled) => _sync[slot] = enabled;

        // A synced deck reports Active and makes the other deck the master â€” enough for the handler's
        // feedback translation; the real lock-state machine lives in the engine.
        public int? SyncMaster
        {
            get
            {
                for (int s = 0; s < _sync.Length; s++)
                    if (_sync[s])
                        return s == 0 ? 1 : 0;
                return null;
            }
        }

        public SyncLockState SyncState(int slot) => _sync[slot] ? SyncLockState.Active : SyncLockState.Off;

        public bool IsQuantizeEnabled(int slot) => _quantize[slot];
        public void SetQuantize(int slot, bool enabled) => _quantize[slot] = enabled;

        public bool IsKeyLockEnabled(int slot) => _keyLock[slot];
        public void SetKeyLock(int slot, bool enabled) => _keyLock[slot] = enabled;

        public int HotCueCount => 8;
        public List<(int Slot, int Index)> HotCues { get; } = new();
        private readonly HashSet<(int, int)> _setCues = new();
        // Optional per-cue display metadata (label/color/auto) the handler should surface in feedback.
        public Dictionary<(int, int), HotCueInfo> CueInfo { get; } = new();
        public bool IsHotCueSet(int slot, int cueIndex) => _setCues.Contains((slot, cueIndex));
        public HotCueInfo GetHotCueInfo(int slot, int cueIndex)
            => CueInfo.TryGetValue((slot, cueIndex), out HotCueInfo info)
                ? info
                : _setCues.Contains((slot, cueIndex)) ? new HotCueInfo(IsSet: true) : HotCueInfo.Unset;
        public void HotCue(int slot, int cueIndex)
        {
            HotCues.Add((slot, cueIndex));
            _setCues.Add((slot, cueIndex));
        }

        public List<(int Slot, int Index)> HotCuesCleared { get; } = new();
        public void ClearHotCue(int slot, int cueIndex)
        {
            HotCuesCleared.Add((slot, cueIndex));
            _setCues.Remove((slot, cueIndex));
        }

        public List<int> Reloads { get; } = new();
        // Simulates the store delivering auto cues on reload: slots 0 and 1 become set for the deck.
        public void ReloadHotCues(int slot)
        {
            Reloads.Add(slot);
            _setCues.Add((slot, 0));
            _setCues.Add((slot, 1));
        }

        public double LoopBeats(int slot) => _loopBeats[slot];
        public bool IsLooping(int slot) => _loopBeats[slot] > 0;
        public void SetLoop(int slot, double beats)
        {
            Loops.Add((slot, beats));
            _loopBeats[slot] = beats;
        }
        public void ClearLoop(int slot)
        {
            LoopsCleared.Add(slot);
            _loopBeats[slot] = 0;
        }

        public List<int> LoopHalved { get; } = new();
        public List<int> LoopDoubled { get; } = new();
        public void HalveLoop(int slot) { LoopHalved.Add(slot); _loopBeats[slot] /= 2; }
        public void DoubleLoop(int slot) { LoopDoubled.Add(slot); _loopBeats[slot] *= 2; }
    }

    [Fact]
    public void LoadTrack_ForwardsValueAsBaseBpmToSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, ActionInputMode.Absolute,
            Value: 128.0, Slot: 1, Argument: @"C:\b.wav"));

        Assert.Equal(128.0, engine.DeckBaseBpm(1), precision: 6);
        Assert.Equal(0.0, engine.DeckBaseBpm(0), precision: 6); // other deck untouched
    }

    [Fact]
    public void DeckPitchBend_RoutesSignedFractionToEngine_ForBendAndRelease()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckPitchBend, ActionInputMode.Absolute, Value: 0.03, Slot: 1)); // bend
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckPitchBend, ActionInputMode.Absolute, Value: 0.0, Slot: 1));  // release

        Assert.Equal(new[] { (1, 0.03), (1, 0.0) }, engine.Bends);
    }

    [Fact]
    public void DeckSetGridBpm_CorrectsGridTempo_WithoutMovingThePitchFader()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, Value: 139.67, Slot: 0, Argument: @"C:\a.wav"));
        // The DJ has pitched the deck; a grid edit must not disturb the audible rate.
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckPitch, ActionInputMode.Absolute, Value: 0.7, Slot: 0));
        double pitchBefore = engine.PitchPosition(0);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSetGridBpm, ActionInputMode.Absolute, Value: 140.0, Slot: 0));

        Assert.Equal(140.0, engine.DeckBaseBpm(0), precision: 6);     // grid/sync tempo corrected
        Assert.Equal(pitchBefore, engine.PitchPosition(0), precision: 6); // pitch fader untouched (inaudible edit)
    }

    [Fact]
    public void HandledKinds_IncludeDeckSetGridBpm()
    {
        Assert.Contains(
            PerformanceActionKind.DeckSetGridBpm,
            new DeckActionHandler(new FakeMultiDeckEngine()).HandledKinds);
    }

    [Fact]
    public void MultiDeck_RoutesActionsToTheRequestedSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckLoadTrack, Argument: @"C:\b.wav", Slot: 1));
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: 1));
        handler.Handle(new PerformanceAction(PerformanceActionKind.TransportStop, Slot: 0));

        Assert.Equal((1, @"C:\b.wav"), Assert.Single(engine.Loaded));
        Assert.Equal(1, Assert.Single(engine.PlayPaused));
        Assert.Equal(0, Assert.Single(engine.Stopped));
    }

    [Fact]
    public void MultiDeck_Feedback_IsPerSlot()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetPlaying(1, true);
        var handler = new DeckActionHandler(engine);

        Assert.False(handler.GetFeedback(PerformanceActionKind.DeckPlayPause, slot: 0).IsActive);
        Assert.True(handler.GetFeedback(PerformanceActionKind.DeckPlayPause, slot: 1).IsActive);
    }

    [Fact]
    public void MultiDeck_OutOfRangeSlot_Throws()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Throws<ArgumentOutOfRangeException>(() => handler.Handle(
            new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: 5)));
    }

    [Fact]
    public void SingleDeck_RejectsNonZeroSlot()
    {
        var handler = new DeckActionHandler(new FakePlaybackEngine());

        Assert.Throws<ArgumentOutOfRangeException>(() => handler.Handle(
            new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: 1)));
    }

    // --- Transport: seek / pitch / cue / sync / quantize ---

    [Fact]
    public void HandledKinds_IncludeTransportControls()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Contains(PerformanceActionKind.DeckSeek, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckJog, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckPitch, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckBpm, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckCue, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckSyncOnce, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckSyncToggle, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckQuantizeToggle, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckKeyLockToggle, handler.HandledKinds);
    }

    [Fact]
    public void Seek_Absolute_PassesPositionToSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSeek, ActionInputMode.Absolute, Value: 0.25, Slot: 1));

        Assert.Equal((1, 0.25, false), Assert.Single(engine.Seeks));
    }

    [Fact]
    public void Seek_Relative_FlagsTheDelta()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSeek, ActionInputMode.Relative, Value: -0.1, Slot: 0));

        Assert.Equal((0, -0.1, true), Assert.Single(engine.Seeks));
    }

    [Fact]
    public void Jog_WhilePaused_UsesPlatterScrubSensitivityAndRaisesSeekFeedback()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(
            engine,
            new JogWheelSettings(PausedSecondsPerRevolution: 1.8, PlayingSecondsPerRevolution: 0.2));
        ActionFeedbackChanged? feedback = null;
        handler.FeedbackChanged += (_, change) =>
        {
            if (change.Kind == PerformanceActionKind.DeckSeek)
                feedback = change;
        };

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckJog, ActionInputMode.Relative, Value: 1.0, Slot: 1));

        Assert.Equal((1, 1.8), Assert.Single(engine.Jogs));
        Assert.NotNull(feedback);
        Assert.Equal(1, feedback!.Slot);
    }

    [Fact]
    public void Jog_WhilePlaying_UsesFineSensitivity()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetPlaying(0, true);
        var handler = new DeckActionHandler(
            engine,
            new JogWheelSettings(PausedSecondsPerRevolution: 1.8, PlayingSecondsPerRevolution: 0.2));

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckJog, ActionInputMode.Relative, Value: -0.5, Slot: 0));

        Assert.Equal((0, -0.1), Assert.Single(engine.Jogs));
    }

    [Fact]
    public void Pitch_RoutesToSlot_WithRelativeFlag()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckPitch, ActionInputMode.Absolute, Value: 0.6, Slot: 1));
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckPitch, ActionInputMode.Relative, Value: 0.02, Slot: 1));

        Assert.Equal(2, engine.Pitches.Count);
        Assert.Equal((1, 0.6, false), engine.Pitches[0]);
        Assert.Equal((1, 0.02, true), engine.Pitches[1]);
    }

    [Fact]
    public void Cue_JumpsTheRequestedSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckCue, Slot: 1));

        Assert.Equal(1, Assert.Single(engine.Cues));
    }

    [Fact]
    public void SyncOnce_AlignsTheRequestedSlot_WithoutLatching()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckSyncOnce, Slot: 1));

        Assert.Equal(1, Assert.Single(engine.SyncOnceCalls));
        Assert.False(handler.GetFeedback(PerformanceActionKind.DeckSyncOnce, slot: 1).IsActive);
    }

    [Fact]
    public void SyncToggle_LatchesAndUnlatchesTheRequestedSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckSyncToggle, Slot: 1));
        Assert.True(engine.IsSyncLocked(1));
        Assert.True(handler.GetFeedback(PerformanceActionKind.DeckSyncToggle, 1).IsActive);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckSyncToggle, Slot: 1));
        Assert.False(engine.IsSyncLocked(1));
        Assert.False(handler.GetFeedback(PerformanceActionKind.DeckSyncToggle, 1).IsActive);
    }

    [Fact]
    public void KeyLockToggle_LatchesAndUnlatchesTheRequestedSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckKeyLockToggle, Slot: 1));
        Assert.True(engine.IsKeyLockEnabled(1));
        Assert.False(engine.IsKeyLockEnabled(0)); // other deck untouched
        Assert.True(handler.GetFeedback(PerformanceActionKind.DeckKeyLockToggle, 1).IsActive);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckKeyLockToggle, Slot: 1));
        Assert.False(engine.IsKeyLockEnabled(1));
        Assert.False(handler.GetFeedback(PerformanceActionKind.DeckKeyLockToggle, 1).IsActive);
    }

    [Fact]
    public void Bpm_RoutesAbsoluteTempoToSlot_AndReportsClampedValue()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetDeckBaseBpm(1, 120.0);
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckBpm, ActionInputMode.Absolute, Value: 140.0, Slot: 1));

        Assert.Equal((1, 140.0), Assert.Single(engine.Bpms));
        ActionFeedbackState feedback = handler.GetFeedback(PerformanceActionKind.DeckBpm, slot: 1);
        Assert.Equal(129.6, feedback.Value, 6);
        Assert.Equal("110.4|129.6", feedback.Argument);
    }

    [Fact]
    public void QuantizeToggle_FlipsPerSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckQuantizeToggle, Slot: 0));

        Assert.True(engine.IsQuantizeEnabled(0));
        Assert.True(handler.GetFeedback(PerformanceActionKind.DeckQuantizeToggle, slot: 0).IsActive);
    }

    [Fact]
    public void PitchFeedback_ReflectsNormalizedPosition()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckPitch, ActionInputMode.Absolute, Value: 0.7, Slot: 0));

        Assert.Equal(0.7, handler.GetFeedback(PerformanceActionKind.DeckPitch, slot: 0).Value, precision: 3);
    }

    [Fact]
    public void HotCue_RoutesIndexFromArgumentToSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckHotCue, Argument: "3", Slot: 1));

        Assert.Equal((1, 3), Assert.Single(engine.HotCues));
    }

    [Fact]
    public void CuePlay_RoutesPressAndReleaseToTheEngine()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);
        Assert.Contains(PerformanceActionKind.DeckCuePlay, handler.HandledKinds);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckCuePlay, Slot: 0, IsPressed: true));
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckCuePlay, Slot: 0, IsPressed: false));

        Assert.Equal(new[] { (0, true), (0, false) }, engine.CuePlays);
    }

    [Fact]
    public void HotCueClear_RoutesIndexToTheEngineClear()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);
        Assert.Contains(PerformanceActionKind.DeckHotCueClear, handler.HandledKinds);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckHotCueClear, Slot: 1, Argument: "3"));

        Assert.Equal((1, 3), Assert.Single(engine.HotCuesCleared));
    }

    [Fact]
    public void HotCueClear_OutOfRange_Throws()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());
        Assert.Throws<ArgumentOutOfRangeException>(() => handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckHotCueClear, Slot: 0, Argument: "99")));
    }

    [Fact]
    public void HotCue_WithoutIndexArgument_Throws()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Throws<ArgumentException>(
            () => handler.Handle(new PerformanceAction(PerformanceActionKind.DeckHotCue, Slot: 0)));
    }

    [Fact]
    public void HotCue_IndexOutOfRange_Throws()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine()); // 8 cues -> valid 0..7

        Assert.Throws<ArgumentOutOfRangeException>(
            () => handler.Handle(new PerformanceAction(PerformanceActionKind.DeckHotCue, Argument: "8", Slot: 0)));
    }

    [Fact]
    public void LoadTrack_RaisesFeedbackCarryingThePath()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);
        ActionFeedbackChanged? captured = null;
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.DeckLoadTrack)
                captured = e;
        };

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, Argument: @"C:\song.flac", Slot: 1));

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Slot);
        Assert.Equal(@"C:\song.flac", captured.State.Argument);
    }

    [Fact]
    public void LoadTrack_EchoesTheAnalyzedBpmInFeedback_SoTheDeckCanDeriveABeatGrid()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);
        ActionFeedbackChanged? captured = null;
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.DeckLoadTrack)
                captured = e;
        };

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, ActionInputMode.Absolute,
            Value: 128.0, Argument: @"C:\song.flac", Slot: 0));

        Assert.NotNull(captured);
        Assert.Equal(128.0, captured!.State.Value, precision: 3);
    }

    [Fact]
    public void LoadTrack_RemainsAvailableThroughFeedback_ForLateUiSubscribers()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());
        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack,
            ActionInputMode.Absolute,
            Value: 128.0,
            Argument: @"C:\song.flac",
            Slot: 1));

        ActionFeedbackState feedback =
            handler.GetFeedback(PerformanceActionKind.DeckLoadTrack, slot: 1);

        Assert.True(feedback.IsAvailable);
        Assert.Equal(@"C:\song.flac", feedback.Argument);
        Assert.Equal(128.0, feedback.Value, precision: 3);
        Assert.False(handler.GetFeedback(
            PerformanceActionKind.DeckLoadTrack, slot: 0).IsAvailable);
    }

    [Fact]
    public void LoadTrack_WhenEngineLoadThrows_SurfacesAFailedFeedback_AndRethrows()
    {
        // The engine cannot open the track (missing/offline file, or the native audio engine fails to
        // create the deck stream — the real bass_fx-missing case). The handler must surface a load-FAILED
        // feedback (IsAvailable:false) so the deck UI shows the failure instead of staying silently empty,
        // then rethrow so the dispatcher still logs the cause (global standards #16/#26).
        var engine = new FakeMultiDeckEngine { ThrowOnLoad = true };
        var handler = new DeckActionHandler(engine);
        ActionFeedbackChanged? captured = null;
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.DeckLoadTrack)
                captured = e;
        };

        Assert.Throws<InvalidOperationException>(() => handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, ActionInputMode.Absolute,
            Value: 128.0, Slot: 1, Argument: @"S:\offline.flac")));

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Slot);
        Assert.False(captured.State.IsAvailable);   // the UI's load-failed signal
        Assert.Equal(@"S:\offline.flac", captured.State.Argument); // names the file that failed
        // GetFeedback reflects the failure too, so a late UI subscriber (tab switch) sees it.
        Assert.False(handler.GetFeedback(PerformanceActionKind.DeckLoadTrack, slot: 1).IsAvailable);
    }

    [Fact]
    public void HotCue_IsInHandledKinds()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Contains(PerformanceActionKind.DeckHotCue, handler.HandledKinds);
    }

    [Fact]
    public void ApplyAutoCues_ReloadsTheDeckSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckApplyAutoCues, Slot: 1));

        Assert.Equal(1, Assert.Single(engine.Reloads));
    }

    [Fact]
    public void ApplyAutoCues_RelightsEveryPad_ViaPerIndexFeedback()
    {
        var engine = new FakeMultiDeckEngine(); // ReloadHotCues sets slots 0 and 1
        var handler = new DeckActionHandler(engine);
        var lit = new List<int>();
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.DeckHotCue && e.Slot == 0 && e.State.IsActive
                && HotCueFeedback.TryDecode(e.State.Argument, out int idx, out HotCueInfo _))
                lit.Add(idx);
        };

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckApplyAutoCues, Slot: 0));

        // One feedback per cue slot is raised; the two reloaded cues report active.
        Assert.Equal(new[] { 0, 1 }, lit);
    }

    [Fact]
    public void HotCue_Feedback_CarriesCueLabelColorAndAutoFlag()
    {
        // A pad must be able to show the cue's name/color and mark a suggestion — so the DeckHotCue feedback
        // encodes the cue's display metadata, not just its lit state.
        var engine = new FakeMultiDeckEngine();
        engine.CueInfo[(0, 2)] = new HotCueInfo(IsSet: true, Label: "Drop", Color: 0xFF3B30, IsAuto: true);
        var handler = new DeckActionHandler(engine);
        HotCueInfo decoded = default;
        int decodedIndex = -1;
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.DeckHotCue && e.Slot == 0)
                HotCueFeedback.TryDecode(e.State.Argument, out decodedIndex, out decoded);
        };

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckHotCue, Argument: "2", Slot: 0));

        Assert.Equal(2, decodedIndex);
        Assert.Equal("Drop", decoded.Label);
        Assert.Equal(0xFF3B30, decoded.Color);
        Assert.True(decoded.IsAuto);
    }

    [Fact]
    public void ApplyAutoCues_IsInHandledKinds()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Contains(PerformanceActionKind.DeckApplyAutoCues, handler.HandledKinds);
    }

    [Fact]
    public void SyncOnce_RaisesMomentaryFeedbackThroughDispatcher()
    {
        var engine = new FakeMultiDeckEngine();
        var dispatcher = new PerformanceActionDispatcher(
            new[] { new DeckActionHandler(engine) },
            NullLogger<PerformanceActionDispatcher>.Instance);

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckSyncOnce, Slot: 1));

        Assert.False(dispatcher.GetFeedback(PerformanceActionKind.DeckSyncOnce, slot: 1).IsActive);
    }

    // --- Loops (DeckSetLoop) ---

    [Fact]
    public void SetLoop_IsInHandledKinds()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Contains(PerformanceActionKind.DeckSetLoop, handler.HandledKinds);
    }

    [Fact]
    public void SetLoop_PositiveBeats_SetsBeatLengthLoopOnSlot()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSetLoop, ActionInputMode.Absolute, Value: 4.0, Slot: 1));

        Assert.Equal((1, 4.0), Assert.Single(engine.Loops));
        Assert.True(engine.IsLooping(1));
    }

    [Fact]
    public void LoopHalveAndDouble_ForwardToTheEngine_AndAreHandled()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);
        Assert.Contains(PerformanceActionKind.DeckLoopHalve, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckLoopDouble, handler.HandledKinds);
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckSetLoop, Value: 4.0, Slot: 1));

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckLoopHalve, Slot: 1));
        Assert.Equal(1, Assert.Single(engine.LoopHalved));

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckLoopDouble, Slot: 1));
        Assert.Equal(1, Assert.Single(engine.LoopDoubled));
    }

    [Fact]
    public void SetLoop_NonPositiveBeats_ClearsTheLoop()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);
        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckSetLoop, Value: 4.0, Slot: 0));

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckSetLoop, Value: 0.0, Slot: 0));

        Assert.Equal(0, Assert.Single(engine.LoopsCleared));
        Assert.False(engine.IsLooping(0));
    }

    [Fact]
    public void SetLoop_RaisesActiveFeedbackCarryingBeatLength()
    {
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckSetLoop, Value: 8.0, Slot: 0));

        ActionFeedbackState fb = handler.GetFeedback(PerformanceActionKind.DeckSetLoop, slot: 0);
        Assert.True(fb.IsActive);
        Assert.Equal(8.0, fb.Value, precision: 6);
    }

    // --- First-beat anchor seam (phase-match input, doc 11) ---

    [Fact]
    public void LoadTrack_DoesNotClaimToKnowFirstBeat_FromTheSingleValueAction()
    {
        // The load action's Value is the BPM; the first-beat anchor is supplied separately, so a plain
        // load leaves the anchor at 0 (phase-match no-op until SetDeckFirstBeat is called).
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckLoadTrack, ActionInputMode.Absolute,
            Value: 128.0, Slot: 0, Argument: @"C:\a.wav"));

        Assert.Equal(0.0, engine.DeckFirstBeat(0), precision: 6);
    }

    [Fact]
    public void DeckSetFirstBeat_IsAHandledKind()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Contains(PerformanceActionKind.DeckSetFirstBeat, handler.HandledKinds);
    }

    [Fact]
    public void DeckSetFirstBeat_ThreadsTheAnchorToTheEngine()
    {
        // The keystone for phase-sync (doc 22 A1): the analyzed first-beat (downbeat) anchor reaches the
        // engine through its own action, so Quantize aligns beats â€” not just tempo â€” instead of snapping
        // to a 0 anchor.
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSetFirstBeat, ActionInputMode.Absolute, Value: 0.347, Slot: 1));

        Assert.Equal(0.347, engine.DeckFirstBeat(1), precision: 6);
    }

    // --- Downbeat (bar-1 "one") anchor seam — display/grid only ---

    [Fact]
    public void DeckSetDownbeat_IsAHandledKind()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Contains(PerformanceActionKind.DeckSetDownbeat, handler.HandledKinds);
    }

    [Fact]
    public void DeckSetDownbeat_RecordsTheAnchor_AndReportsItBackAsFeedback()
    {
        // The downbeat is display-only: it must NOT reach the engine's first-beat (phase) anchor, only be
        // stored and echoed so the deck UI can re-anchor its bar markers on the one.
        var engine = new FakeMultiDeckEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckSetDownbeat, ActionInputMode.Absolute, Value: 0.55, Slot: 1));

        Assert.Equal(0.55, handler.GetFeedback(PerformanceActionKind.DeckSetDownbeat, 1).Value, precision: 6);
        Assert.True(handler.GetFeedback(PerformanceActionKind.DeckSetDownbeat, 1).IsAvailable);
        // Independent of the first-beat (beat-phase) anchor — setting the bar never moves the beats.
        Assert.Equal(0.0, engine.DeckFirstBeat(1), precision: 6);
    }

    [Fact]
    public void DeckSetDownbeat_DefaultsToZero_BeforeAnyEdit()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());

        Assert.Equal(0.0, handler.GetFeedback(PerformanceActionKind.DeckSetDownbeat, 0).Value, precision: 6);
    }

    // --- DeckBpmNudge ---

    [Fact]
    public void DeckBpmNudge_IsInHandledKinds()
    {
        var handler = new DeckActionHandler(new FakeMultiDeckEngine());
        Assert.Contains(PerformanceActionKind.DeckBpmNudge, handler.HandledKinds);
    }

    [Fact]
    public void DeckBpmNudge_PositiveDelta_IncreasesDecBpmByDelta()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetDeckBaseBpm(0, 120.0); // range = 110.4..129.6
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative, Value: 0.1, Slot: 0));

        Assert.Equal(120.1, engine.DeckBpm(0), precision: 6);
    }

    [Fact]
    public void DeckBpmNudge_NegativeDelta_ReducesDeckBpmByDelta()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetDeckBaseBpm(0, 120.0);
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative, Value: -0.1, Slot: 0));

        Assert.Equal(119.9, engine.DeckBpm(0), precision: 6);
    }

    [Fact]
    public void DeckBpmNudge_ClampsAtMaximum_WhenDeltaWouldExceedRange()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetDeckBaseBpm(0, 120.0); // max = 129.6
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative, Value: 50.0, Slot: 0));

        // engine.SetDeckBpm clamps internally to MaximumDeckBpm
        Assert.Equal(engine.MaximumDeckBpm(0), engine.DeckBpm(0), precision: 6);
    }

    [Fact]
    public void DeckBpmNudge_ClampsAtMinimum_WhenDeltaWouldGoBelowRange()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetDeckBaseBpm(0, 120.0); // min = 110.4
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative, Value: -50.0, Slot: 0));

        Assert.Equal(engine.MinimumDeckBpm(0), engine.DeckBpm(0), precision: 6);
    }

    [Fact]
    public void DeckBpmNudge_IsPerSlot_DoesNotAffectOtherDeck()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetDeckBaseBpm(0, 120.0);
        engine.SetDeckBaseBpm(1, 128.0);
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative, Value: 1.0, Slot: 0));

        Assert.Equal(121.0, engine.DeckBpm(0), precision: 6);
        Assert.Equal(128.0, engine.DeckBpm(1), precision: 6); // untouched
    }

    [Fact]
    public void DeckBpmNudge_RaisesBpmFeedback_WithUpdatedValue()
    {
        var engine = new FakeMultiDeckEngine();
        engine.SetDeckBaseBpm(0, 120.0);
        var handler = new DeckActionHandler(engine);
        ActionFeedbackChanged? raised = null;
        handler.FeedbackChanged += (_, e) =>
        {
            if (e.Kind == PerformanceActionKind.DeckBpm && e.Slot == 0)
                raised = e;
        };

        handler.Handle(new PerformanceAction(
            PerformanceActionKind.DeckBpmNudge, ActionInputMode.Relative, Value: 0.5, Slot: 0));

        Assert.NotNull(raised);
        Assert.Equal(120.5, raised!.State.Value, precision: 6);
    }
}
