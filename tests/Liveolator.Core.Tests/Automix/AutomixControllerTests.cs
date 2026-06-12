using Liveolator.Core.Actions;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Automix;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Core.Tests.Automix;

public class AutomixControllerTests
{
    // Geometry used throughout: deck A (FROM) at 120 BPM, first beat 0.4 s ⇒ beat = 0.5 s. At the
    // starting position 8.0 s the leader sits at beat 15.2, so the blend's quantized start is beat 16
    // (position 8.4 s) and elapsed beats e ⇔ position 8.4 + 0.5·e. Duration = 2 bars = 8 beats.
    private readonly FakeAutomixDeckReader _decks = new();
    private readonly FakeDispatcher _dispatcher = new();
    private readonly AutomixController _controller;

    public AutomixControllerTests()
    {
        _controller = new AutomixController(_decks);
        _controller.Attach(_dispatcher);

        // Deck A playing mid-track, deck B loaded and parked; crossfader fully on A.
        _decks.Set(0, Snapshot(isPlaying: true, positionSeconds: 8.0));
        _decks.Set(1, Snapshot(isPlaying: false, positionSeconds: 0.0));
        _decks.Mixer = MixerState.Default.WithCrossfader(0.0);
        _controller.SetDurationKnob(0.0); // 2 bars = 8 beats — keeps the timeline short
    }

    private static AutomixDeckSnapshot Snapshot(
        bool isPlaying,
        double positionSeconds,
        double bpm = 120.0,
        double firstBeat = 0.4,
        double length = 300.0,
        SyncLockState syncState = SyncLockState.Off,
        bool syncLocked = false)
        => new(
            IsLoaded: length > 0.0, IsPlaying: isPlaying, BaseBpm: bpm, EffectiveBpm: bpm,
            FirstBeatSeconds: firstBeat, PositionSeconds: positionSeconds, LengthSeconds: length,
            SyncState: syncState, SyncLocked: syncLocked);

    // One pump tick with the OUTGOING playhead at the given position — the blend's only pacemaker.
    private void TickAtFromPosition(double positionSeconds)
    {
        _decks.Set(0, Snapshot(isPlaying: true, positionSeconds: positionSeconds));
        _controller.OnMasterClockTick(0);
    }

    private IReadOnlyList<PerformanceAction> Emitted(PerformanceActionKind kind)
        => _dispatcher.Dispatched.Where(a => a.Origin == AutomixController.OriginTag && a.Kind == kind).ToList();

    private void RunToTransitioning()
    {
        _controller.Toggle();                       // sync + seek + play, immediately
        _decks.Set(1, Snapshot(isPlaying: true, positionSeconds: 0.4, syncState: SyncLockState.Locked,
            syncLocked: true));
        TickAtFromPosition(8.4);                    // the quantized start beat (elapsed 0)
    }

    [Fact]
    public void Toggle_LaunchesTheIncomingDeckImmediately_SyncSeekAndPlay()
    {
        _controller.Toggle();

        Assert.Equal(AutomixPhase.Transitioning, _controller.Phase);
        // Seeked to its mix-in point (the first-beat anchor as a fraction), synced, and started — NOW,
        // not on some future downbeat: the blend runs while sync converges (owner direction).
        PerformanceAction seek = Assert.Single(Emitted(PerformanceActionKind.DeckSeek));
        Assert.Equal(1, seek.Slot);
        Assert.Equal(0.4 / 300.0, seek.Value, precision: 9);
        PerformanceAction sync = Assert.Single(Emitted(PerformanceActionKind.DeckSyncToggle));
        Assert.Equal(1, sync.Slot);
        PerformanceAction play = Assert.Single(Emitted(PerformanceActionKind.DeckPlayPause));
        Assert.Equal(1, play.Slot);
        // The crossfader is not yanked on engage; the ramp starts from its current position.
        Assert.Empty(Emitted(PerformanceActionKind.MixerCrossfade));
    }

    [Fact]
    public void Ramp_StartsFromTheCurrentCrossfaderPosition()
    {
        _decks.Mixer = MixerState.Default.WithCrossfader(0.3);
        _controller.Toggle();

        TickAtFromPosition(8.4); // the quantized start (progress 0)

        Assert.Equal(0.3, Emitted(PerformanceActionKind.MixerCrossfade)[^1].Value, precision: 9);
    }

    [Fact]
    public void Toggle_IncomingAlreadyRolling_RespectsItsPositionAndTransport()
    {
        // The performer cued deck B early and it is already playing: no seek, no play — just sync.
        _decks.Set(1, Snapshot(isPlaying: true, positionSeconds: 12.0));

        _controller.Toggle();

        Assert.Equal(AutomixPhase.Transitioning, _controller.Phase);
        Assert.Empty(Emitted(PerformanceActionKind.DeckSeek));
        Assert.Empty(Emitted(PerformanceActionKind.DeckPlayPause));
        Assert.Single(Emitted(PerformanceActionKind.DeckSyncToggle));
    }

    [Fact]
    public void Toggle_NothingPlaying_RefusesWithATypedReason()
    {
        _decks.Set(0, Snapshot(isPlaying: false, positionSeconds: 8.0));

        _controller.Toggle();

        Assert.Equal(AutomixPhase.Idle, _controller.Phase);
        Assert.Equal(AutomixRefusal.NothingPlaying, _controller.LastRefusal);
        Assert.Empty(_dispatcher.Dispatched);
    }

    [Fact]
    public void Blend_AdvancesWhileSyncIsStillConverging()
    {
        // The crossfade must crawl WHILE the sync loop converges — not wait for a confirmed lock.
        _controller.Toggle();
        _decks.Set(1, Snapshot(isPlaying: true, positionSeconds: 0.4, syncState: SyncLockState.Active,
            syncLocked: true));

        TickAtFromPosition(9.4); // 2 of 8 beats past the quantized start, still not Locked

        Assert.Equal(0.25, _controller.Progress, precision: 9);
        Assert.Equal(0.25, Emitted(PerformanceActionKind.MixerCrossfade)[^1].Value, precision: 9);
    }

    [Fact]
    public void Blend_HoldsAtZeroUntilTheLeaderCrossesItsNextBeat()
    {
        // Engaged at beat 15.2: progress stays 0 until the leader reaches beat 16, so the style
        // midpoints stay beat-quantized without delaying the engage itself.
        _controller.Toggle();

        TickAtFromPosition(8.0);
        Assert.Equal(0.0, _controller.Progress, precision: 9);
        TickAtFromPosition(8.9); // beat 17 = 1 of 8 beats in

        Assert.Equal(0.125, _controller.Progress, precision: 9);
    }

    [Fact]
    public void Blend_NeverRunsBackwards_WhenTheLeaderPlayheadJumpsBack()
    {
        // A loop wrap (or seek jitter) on the outgoing deck must not pull the blend backwards.
        RunToTransitioning();
        TickAtFromPosition(9.4); // progress 0.25

        TickAtFromPosition(8.6); // playhead jumped back

        Assert.Equal(0.25, _controller.Progress, precision: 9);
    }

    [Fact]
    public void Transitioning_DrivesTheCrossfaderAlongTheProfile()
    {
        RunToTransitioning();
        Assert.Equal(AutomixPhase.Transitioning, _controller.Phase);

        TickAtFromPosition(9.4); // 2 of 8 beats in → progress 0.25

        Assert.Equal(0.25, _controller.Progress, precision: 9);
        PerformanceAction latest = Emitted(PerformanceActionKind.MixerCrossfade)[^1];
        Assert.Equal(0.25, latest.Value, precision: 9);
    }

    [Fact]
    public void Transitioning_Completes_LandsTheFloorAndRetiresTheOutgoingDeck()
    {
        RunToTransitioning();

        TickAtFromPosition(12.4); // 8 of 8 beats → progress 1.0 → finish

        Assert.Equal(AutomixPhase.Idle, _controller.Phase);
        Assert.Equal(1.0, Emitted(PerformanceActionKind.MixerCrossfade)[^1].Value, precision: 9);
        // The outgoing deck is paused (position kept); the incoming deck's SYNC latch is released.
        Assert.Contains(Emitted(PerformanceActionKind.DeckPlayPause), a => a.Slot == 0);
        Assert.Contains(Emitted(PerformanceActionKind.DeckSyncToggle), a => a.Slot == 1);
        // A pure crossfade never touches the channel strips.
        Assert.Empty(Emitted(PerformanceActionKind.MixerEqBand));
        Assert.Empty(Emitted(PerformanceActionKind.MixerFilter));
    }

    [Fact]
    public void PerformerGesture_OnTheCrossfader_IsAnInstantSilentHandover()
    {
        RunToTransitioning();
        int dispatchedBefore = _dispatcher.Dispatched.Count;

        // A human moves the crossfader (no origin stamp) — automation must freeze and yield.
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, 0.7));

        Assert.Equal(AutomixPhase.Idle, _controller.Phase);
        TickAtFromPosition(10.4);
        // Nothing new from automix after the takeover — no snap-back, no re-grab.
        Assert.Equal(dispatchedBefore + 1, _dispatcher.Dispatched.Count);
    }

    [Fact]
    public void PerformerGesture_OnTheInvolvedDeckTransport_AlsoYields()
    {
        RunToTransitioning();

        _dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause, Slot: 0));

        Assert.Equal(AutomixPhase.Idle, _controller.Phase);
    }

    [Fact]
    public void OwnStampedActions_DoNotTriggerTheTakeoverRule()
    {
        RunToTransitioning();

        TickAtFromPosition(8.9); // emits automix-stamped mixer actions through the dispatcher

        Assert.Equal(AutomixPhase.Transitioning, _controller.Phase);
    }

    [Fact]
    public void ToggleWhileActive_Aborts()
    {
        RunToTransitioning();

        _controller.Toggle();

        Assert.Equal(AutomixPhase.Idle, _controller.Phase);
    }

    [Fact]
    public void BarAlignment_SeeksTheIncomingDeck_WhenItLockedOntoTheWrongBeatOfTheBar()
    {
        _controller.Toggle();
        // Beat-locked, but one beat (0.5 s at 120 BPM) into its bar relative to the leader's grid:
        // leader on a downbeat (beat 16 = bar 4), incoming at anchor+0.5 s (beat 2 of its bar).
        _decks.Set(1, Snapshot(isPlaying: true, positionSeconds: 0.9, syncState: SyncLockState.Locked,
            syncLocked: true));

        TickAtFromPosition(8.4);  // locked ×1 (progress 0 — still in the quiet start)
        TickAtFromPosition(8.4);  // locked ×2 → one-shot bar alignment (leader exactly on beat 16)

        PerformanceAction correction = Emitted(PerformanceActionKind.DeckSeek)[^1];
        Assert.Equal(1, correction.Slot);
        Assert.Equal(ActionInputMode.Relative, correction.InputMode);
        Assert.Equal(-0.5 / 300.0, correction.Value, precision: 9); // back one beat onto the downbeat
    }

    [Fact]
    public void DurationKnob_ResolvesToDetentsAndReportsBars()
    {
        _controller.SetDurationKnob(0.6);
        Assert.Equal(16, _controller.RequestedBars);

        _controller.SetDurationKnob(1.0);
        Assert.Equal(64, _controller.RequestedBars);
    }

    // ----- fakes -----

    private sealed class FakeAutomixDeckReader : IAutomixDeckReader
    {
        private readonly AutomixDeckSnapshot[] _snapshots = new AutomixDeckSnapshot[2];

        public void Set(int slot, AutomixDeckSnapshot snapshot) => _snapshots[slot] = snapshot;

        public AutomixDeckSnapshot ReadDeck(int slot) => _snapshots[slot];

        public MixerState Mixer { get; set; } = MixerState.Default;
    }

    private sealed class FakeDispatcher : IPerformanceActionDispatcher
    {
        public List<PerformanceAction> Dispatched { get; } = new();

        public event EventHandler<ActionFeedbackChanged>? FeedbackChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<PerformanceAction>? ActionDispatched;

        public void Dispatch(PerformanceAction action)
        {
            Dispatched.Add(action);
            ActionDispatched?.Invoke(this, action);
        }

        public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
            => ActionFeedbackState.Unavailable;
    }
}
