using Liveolator.Core.Actions;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Automix;
using Liveolator.Core.Beat;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Core.Tests.Automix;

public class AutomixControllerTests
{
    private readonly FakeBeatClock _clock = new();
    private readonly FakeAutomixDeckReader _decks = new();
    private readonly FakeDispatcher _dispatcher = new();
    private readonly AutomixController _controller;

    public AutomixControllerTests()
    {
        _controller = new AutomixController(_clock, _decks);
        _controller.Attach(_dispatcher);

        // Deck A playing mid-track on a bar boundary, deck B loaded and parked — the standard hand-over.
        _decks.Set(0, Snapshot(isPlaying: true, positionSeconds: 8.0));
        _decks.Set(1, Snapshot(isPlaying: false, positionSeconds: 0.0));
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

    private void Tick(double beat, int bar)
    {
        _clock.Current = BeatClockState.Idle with
        {
            Bpm = 120.0,
            BeatCount = (int)Math.Floor(beat),
            BeatPhase = beat - Math.Floor(beat),
            BarNumber = bar,
            IsLocked = true,
            Source = BeatClockSource.Deck,
        };
        _controller.OnMasterClockTick(0);
    }

    private IReadOnlyList<PerformanceAction> Emitted(PerformanceActionKind kind)
        => _dispatcher.Dispatched.Where(a => a.Origin == AutomixController.OriginTag && a.Kind == kind).ToList();

    private void RunToTransitioning()
    {
        _controller.Toggle();                       // Arming
        Tick(beat: 0.0, bar: 0);                    // establish the bar reference
        Tick(beat: 4.0, bar: 1);                    // downbeat → incoming deck launched, Syncing
        _decks.Set(1, Snapshot(isPlaying: true, positionSeconds: 0.4, syncState: SyncLockState.Locked,
            syncLocked: true));
        Tick(beat: 4.2, bar: 1);                    // locked ×1
        Tick(beat: 4.4, bar: 1);                    // locked ×2 (confirmed) + bar-align check
        Tick(beat: 8.0, bar: 2);                    // downbeat after confirmed lock → Transitioning
    }

    [Fact]
    public void Toggle_ArmsAndPreparesTheIncomingDeckSilently()
    {
        _controller.Toggle();

        Assert.Equal(AutomixPhase.Arming, _controller.Phase);
        // Entry frame: crossfader hard on the outgoing side (deck A = 0.0) while nothing is audible yet.
        PerformanceAction crossfade = Assert.Single(Emitted(PerformanceActionKind.MixerCrossfade));
        Assert.Equal(0.0, crossfade.Value, precision: 9);
        // SYNC is engaged on the incoming deck through the same action a human would press.
        PerformanceAction sync = Assert.Single(Emitted(PerformanceActionKind.DeckSyncToggle));
        Assert.Equal(1, sync.Slot);
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
    public void Arming_LaunchesTheIncomingDeckOnTheLeaderDownbeat_FromItsMixInPoint()
    {
        _controller.Toggle();
        Tick(beat: 0.0, bar: 0);
        Assert.Empty(Emitted(PerformanceActionKind.DeckPlayPause)); // not before a downbeat

        Tick(beat: 4.0, bar: 1);

        PerformanceAction seek = Assert.Single(Emitted(PerformanceActionKind.DeckSeek));
        Assert.Equal(1, seek.Slot);
        Assert.Equal(0.4 / 300.0, seek.Value, precision: 9); // the first-beat anchor as a fraction
        PerformanceAction play = Assert.Single(Emitted(PerformanceActionKind.DeckPlayPause));
        Assert.Equal(1, play.Slot);
        Assert.Equal(AutomixPhase.Syncing, _controller.Phase);
    }

    [Fact]
    public void Syncing_TheBlendDoesNotStartUntilTheLockIsConfirmed()
    {
        _controller.Toggle();
        Tick(beat: 0.0, bar: 0);
        Tick(beat: 4.0, bar: 1);
        _decks.Set(1, Snapshot(isPlaying: true, positionSeconds: 0.4, syncState: SyncLockState.Active));

        // Downbeats keep passing but the deck never reports Locked — the blend must not begin.
        Tick(beat: 8.0, bar: 2);
        Tick(beat: 12.0, bar: 3);

        Assert.Equal(AutomixPhase.Syncing, _controller.Phase);
    }

    [Fact]
    public void Syncing_LockTimeout_AbortsAndRetiresTheDeckItStarted()
    {
        _controller.Toggle();
        Tick(beat: 0.0, bar: 0);
        Tick(beat: 4.0, bar: 1);
        _decks.Set(1, Snapshot(isPlaying: true, positionSeconds: 0.4, syncState: SyncLockState.Active,
            syncLocked: true));

        // Default timeout = 4 bars = 16 beats past the sync start (beat 4).
        Tick(beat: 21.0, bar: 5);

        Assert.Equal(AutomixPhase.Idle, _controller.Phase);
        // The deck WE started is paused and its SYNC released; the playing deck A was never touched.
        Assert.Equal(2, Emitted(PerformanceActionKind.DeckPlayPause).Count); // launch + retire
        Assert.Equal(2, Emitted(PerformanceActionKind.DeckSyncToggle).Count); // engage + release
        Assert.All(Emitted(PerformanceActionKind.DeckPlayPause), a => Assert.Equal(1, a.Slot));
    }

    [Fact]
    public void Transitioning_DrivesTheCrossfaderAlongTheProfile()
    {
        RunToTransitioning();
        Assert.Equal(AutomixPhase.Transitioning, _controller.Phase);

        Tick(beat: 10.0, bar: 2); // 2 of 8 beats in → progress 0.25

        Assert.Equal(0.25, _controller.Progress, precision: 9);
        PerformanceAction latest = Emitted(PerformanceActionKind.MixerCrossfade)[^1];
        Assert.Equal(0.25, latest.Value, precision: 9);
    }

    [Fact]
    public void Transitioning_Completes_LandsTheFloorAndRetiresTheOutgoingDeck()
    {
        RunToTransitioning();

        Tick(beat: 16.0, bar: 4); // 8 of 8 beats → progress 1.0 → finish

        Assert.Equal(AutomixPhase.Idle, _controller.Phase);
        Assert.Equal(1.0, Emitted(PerformanceActionKind.MixerCrossfade)[^1].Value, precision: 9);
        // The outgoing deck is paused (position kept); the incoming deck's SYNC latch is released.
        Assert.Contains(Emitted(PerformanceActionKind.DeckPlayPause), a => a.Slot == 0);
        Assert.Contains(Emitted(PerformanceActionKind.DeckSyncToggle), a => a.Slot == 1);
        // The outgoing channel strip is handed back exactly as the performer had it (flat defaults).
        Assert.Equal(3, Emitted(PerformanceActionKind.MixerEqBand).Count(a => a.Slot == 0 && a.Value == 0.5));
        Assert.Contains(Emitted(PerformanceActionKind.MixerFilter), a => a.Slot == 0 && a.Value == 0.5);
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
        Tick(beat: 12.0, bar: 3);
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

        Tick(beat: 9.0, bar: 2); // emits automix-stamped mixer actions through the dispatcher

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
    public void EqMixEntry_KillsTheIncomingBassBeforeAnythingIsAudible()
    {
        _controller.SetStyle(AutomixStyle.EqMix);

        _controller.Toggle();

        PerformanceAction toLow = Assert.Single(
            Emitted(PerformanceActionKind.MixerEqBand), a => a.Slot == 1 && a.Argument == "Low");
        Assert.Equal(0.0, toLow.Value, precision: 9);
    }

    [Fact]
    public void BarAlignment_SeeksTheIncomingDeck_WhenItLockedOntoTheWrongBeatOfTheBar()
    {
        _controller.Toggle();
        Tick(beat: 0.0, bar: 0);
        Tick(beat: 4.0, bar: 1);
        // Beat-locked, but one beat (0.5 s at 120 BPM) into its bar relative to the leader's grid:
        // leader at anchor+8.0 s (a downbeat), incoming at anchor+0.5 s (beat 2 of its bar).
        _decks.Set(0, Snapshot(isPlaying: true, positionSeconds: 8.4));
        _decks.Set(1, Snapshot(isPlaying: true, positionSeconds: 0.9, syncState: SyncLockState.Locked,
            syncLocked: true));

        Tick(beat: 4.2, bar: 1);
        Tick(beat: 4.4, bar: 1); // lock confirmed → one-shot bar alignment

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

    private sealed class FakeBeatClock : IBeatClock
    {
        public BeatClockState Current { get; set; } = BeatClockState.Idle;

        public event EventHandler<BeatClockState>? StateChanged
        {
            add { }
            remove { }
        }
    }

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
