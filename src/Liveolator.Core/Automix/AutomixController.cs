using Liveolator.Core.Actions;
using Liveolator.Core.Audio.Sync;
using Liveolator.Core.Beat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Automix;

/// <summary>
/// The auto-mix engine (doc 11 Auto-Mix): a one-button, beat-locked deck-to-deck transition driven
/// entirely off the ONE shared beat clock. Ticked by the <see cref="MasterClockBridge"/> on the same
/// 10 ms pump that runs sync correction; reads decks/mixer through <see cref="IAutomixDeckReader"/>;
/// writes ONLY <see cref="PerformanceAction"/>s (stamped <see cref="OriginTag"/>) — automation of
/// the same controls a human uses, through the same dispatcher.
/// </summary>
/// <remarks>
/// Safety invariants (advisor spec §5): the audible blend cannot begin until the incoming deck
/// REPORTS a confirmed beat lock; any human gesture on a watched control is an instant, silent
/// handover (freeze, never snap back, never re-grab); and no failure path ever modifies the deck
/// that is currently playing — an auto-mix failure must be inaudible.
/// </remarks>
public sealed class AutomixController : IMasterClockTickListener, IDisposable
{
    /// <summary>The <see cref="PerformanceAction.Origin"/> stamp on every action this engine emits.</summary>
    public const string OriginTag = "automix";

    // Hard watchdog: no phase may outlive this many pump ticks (~60 s at 10 ms) even if the clock
    // stops advancing — beats are the normal timeout currency, but a dead clock must not wedge us.
    private const int MaxTicksPerPhase = 6_000;

    private static readonly CrossFadeProfile CrossFade = new();
    private static readonly EqMixProfile EqMix = new();
    private static readonly FxMixProfile FxMix = new();

    private static readonly IReadOnlySet<PerformanceActionKind> WatchedMixerKinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.MixerCrossfade,
        PerformanceActionKind.MixerEqBand,
        PerformanceActionKind.MixerFilter,
    };

    private static readonly IReadOnlySet<PerformanceActionKind> WatchedDeckKinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.DeckPlayPause,
        PerformanceActionKind.DeckSeek,
        PerformanceActionKind.DeckJog,
        PerformanceActionKind.DeckPitch,
        PerformanceActionKind.DeckBpm,
        PerformanceActionKind.DeckBpmNudge,
        PerformanceActionKind.DeckSyncOnce,
        PerformanceActionKind.DeckSyncToggle,
        PerformanceActionKind.DeckLoadTrack,
        PerformanceActionKind.DeckCue,
        PerformanceActionKind.DeckHotCue,
        PerformanceActionKind.DeckSetLoop,
    };

    private readonly IBeatClock _clock;
    private readonly IAutomixDeckReader _decks;
    private readonly AutomixSettings _settings;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private IPerformanceActionDispatcher? _dispatcher;
    private bool _disposed;

    // --- state under _gate ---
    private AutomixPhase _phase = AutomixPhase.Idle;
    private AutomixPlan? _plan;
    private IAutomixStyleProfile? _profile;
    private AutomixTransitionShape? _shape;
    private double _knob = AutomixDurationKnob.KnobFor(AutomixDurationKnob.DefaultBars);
    private int _requestedBars = AutomixDurationKnob.DefaultBars;
    private AutomixStyle _style = AutomixStyle.CrossFade;
    private AutomixRefusal _lastRefusal = AutomixRefusal.None;
    private double _syncStartBeat;
    private double _startBeat;
    private double _lastProgress;
    private int _lockedTicks;
    private int _lastBarNumber;
    private bool _hasBarRef;
    private bool _barAlignChecked;
    private bool _startedIncoming;
    private int _ticksInPhase;
    private Mixer.DeckChannelState? _fromRestore;
    private AutomixFrame? _lastFrame;

    public AutomixController(
        IBeatClock clock,
        IAutomixDeckReader decks,
        AutomixSettings? settings = null,
        ILoggerFactory? loggerFactory = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _decks = decks ?? throw new ArgumentNullException(nameof(decks));
        _settings = settings ?? AutomixSettings.Default;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<AutomixController>();
    }

    /// <summary>Raised after any observable state change (phase, knob, style, refusal) for feedback.</summary>
    public event EventHandler? Changed;

    public AutomixPhase Phase
    {
        get { lock (_gate) return _phase; }
    }

    /// <summary>Transition progress 0..1 while transitioning; 0 otherwise.</summary>
    public double Progress
    {
        get { lock (_gate) return _lastProgress; }
    }

    /// <summary>The duration knob position 0..1 (resolved to <see cref="RequestedBars"/>).</summary>
    public double DurationKnob
    {
        get { lock (_gate) return _knob; }
    }

    /// <summary>The knob-selected transition length in bars (the NEXT transition's length).</summary>
    public int RequestedBars
    {
        get { lock (_gate) return _requestedBars; }
    }

    public AutomixStyle Style
    {
        get { lock (_gate) return _style; }
    }

    /// <summary>Why the last engage attempt refused, for the UI/log; None after a successful start.</summary>
    public AutomixRefusal LastRefusal
    {
        get { lock (_gate) return _lastRefusal; }
    }

    /// <summary>
    /// Connect the dispatcher this engine emits through and observe live input for the performer
    /// takeover rule. Called once at composition, after the dispatcher exists (the handler that
    /// feeds this controller is itself part of the dispatcher's handler set).
    /// </summary>
    public void Attach(IPerformanceActionDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        lock (_gate)
        {
            if (_dispatcher is not null)
                _dispatcher.ActionDispatched -= OnActionDispatched;
            _dispatcher = dispatcher;
        }
        dispatcher.ActionDispatched += OnActionDispatched;
    }

    /// <summary>Start a transition (when idle) or abort the one in flight (freeze, hand over).</summary>
    public void Toggle()
    {
        var actions = new List<PerformanceAction>();
        lock (_gate)
        {
            if (_phase == AutomixPhase.Idle)
                TryStartLocked(actions);
            else
                AbortLocked(stopIncoming: false, "performer pressed AUTOMIX", actions);
        }
        DispatchAll(actions);
        RaiseChanged();
    }

    /// <summary>Set the transition length from the 0..1 TIME knob (applies to the next transition).</summary>
    public void SetDurationKnob(double knobPosition)
    {
        lock (_gate)
        {
            _knob = Math.Clamp(knobPosition, 0.0, 1.0);
            _requestedBars = AutomixDurationKnob.BarsFor(_knob);
        }
        RaiseChanged();
    }

    public void SetStyle(AutomixStyle style)
    {
        lock (_gate) _style = style;
        RaiseChanged();
    }

    /// <inheritdoc />
    public void OnMasterClockTick(long hostTimeTicks)
    {
        var actions = new List<PerformanceAction>();
        bool changed = false;
        lock (_gate)
        {
            if (_phase == AutomixPhase.Idle || _plan is null || _shape is null || _profile is null)
                return;

            _ticksInPhase++;
            if (_ticksInPhase > MaxTicksPerPhase)
            {
                AbortLocked(_startedIncoming, "phase watchdog expired (clock not advancing?)", actions);
                changed = true;
            }
            else
            {
                BeatClockState state = _clock.Current;
                double beatNow = state.BeatCount + Math.Clamp(state.BeatPhase, 0.0, 1.0);
                bool downbeat = _hasBarRef && state.BarNumber != _lastBarNumber;
                _lastBarNumber = state.BarNumber;
                _hasBarRef = true;

                changed = _phase switch
                {
                    AutomixPhase.Arming => TickArmingLocked(downbeat, beatNow, actions),
                    AutomixPhase.Syncing => TickSyncingLocked(downbeat, beatNow, actions),
                    AutomixPhase.Transitioning => TickTransitioningLocked(beatNow, actions),
                    _ => false,
                };
            }
        }
        DispatchAll(actions);
        if (changed)
            RaiseChanged();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_dispatcher is not null)
                _dispatcher.ActionDispatched -= OnActionDispatched;
        }
    }

    // ----- engage -----

    private void TryStartLocked(List<PerformanceAction> actions)
    {
        if (_dispatcher is null)
        {
            _logger.LogError("Auto-mix engaged before a dispatcher was attached; ignoring.");
            return;
        }

        AutomixDeckSnapshot a = _decks.ReadDeck(0);
        AutomixDeckSnapshot b = _decks.ReadDeck(1);
        if (!a.IsPlaying && !b.IsPlaying)
        {
            _lastRefusal = AutomixRefusal.NothingPlaying;
            _logger.LogWarning("Auto-mix refused: nothing is playing.");
            return;
        }

        // FROM = the audible-dominant deck: the only playing one, else the louder crossfader side.
        int fromSlot = a.IsPlaying && b.IsPlaying
            ? (_decks.Mixer.Crossfader <= 0.5 ? 0 : 1)
            : (a.IsPlaying ? 0 : 1);
        int toSlot = 1 - fromSlot;
        AutomixDeckSnapshot from = fromSlot == 0 ? a : b;
        AutomixDeckSnapshot to = fromSlot == 0 ? b : a;

        AutomixPlan plan = AutomixPreflight.Plan(from, fromSlot, to, toSlot, _requestedBars, _style, _settings);
        _lastRefusal = plan.Refusal;
        if (!plan.IsAllowed)
        {
            _logger.LogWarning("Auto-mix refused: {Reason}.", plan.Refusal);
            return;
        }

        _plan = plan;
        _profile = ProfileFor(plan.EffectiveStyle);
        double fromSide = fromSlot == 0 ? 0.0 : 1.0;
        _shape = new AutomixTransitionShape(fromSide, 1.0 - fromSide, plan.PlannedBars * _settings.BeatsPerBar);
        _fromRestore = _decks.Mixer.Channel(fromSlot);
        _lastFrame = null;
        _startedIncoming = false;
        _lockedTicks = 0;
        _barAlignChecked = false;
        _hasBarRef = false;
        _ticksInPhase = 0;
        _lastProgress = 0.0;

        // Pre-set the style's entry values while the incoming deck is still inaudible: crossfader to
        // the outgoing extreme, and (EQ MIX) the incoming low band killed before it ever opens.
        DiffFrameLocked(_profile.Evaluate(0.0, _shape), actions);
        if (!to.SyncLocked)
            actions.Add(Deck(PerformanceActionKind.DeckSyncToggle, toSlot));

        _phase = AutomixPhase.Arming;
        _logger.LogInformation(
            "Auto-mix armed: deck {From} → deck {To}, {Bars} bars, style {Style} (requested {Requested}).",
            fromSlot, toSlot, plan.PlannedBars, plan.EffectiveStyle, _style);
    }

    // ----- phases -----

    private bool TickArmingLocked(bool downbeat, double beatNow, List<PerformanceAction> actions)
    {
        if (!downbeat)
            return false;

        // Launch the incoming deck on the leader's downbeat, from its mix-in point. Starting a deck
        // already rolling (the performer cued it early) is respected — no seek, no restart.
        AutomixDeckSnapshot to = _decks.ReadDeck(_plan!.ToSlot);
        if (!to.IsPlaying)
        {
            if (to.LengthSeconds > 0.0)
                actions.Add(Deck(PerformanceActionKind.DeckSeek, _plan.ToSlot,
                    ActionInputMode.Absolute, _plan.MixInSeconds / to.LengthSeconds));
            actions.Add(Deck(PerformanceActionKind.DeckPlayPause, _plan.ToSlot));
            _startedIncoming = true;
        }

        _syncStartBeat = beatNow;
        _phase = AutomixPhase.Syncing;
        return true;
    }

    private bool TickSyncingLocked(bool downbeat, double beatNow, List<PerformanceAction> actions)
    {
        AutomixDeckSnapshot to = _decks.ReadDeck(_plan!.ToSlot);
        _lockedTicks = to.SyncState == SyncLockState.Locked ? _lockedTicks + 1 : 0;

        bool confirmed = _lockedTicks >= _settings.LockConfirmTicks;
        if (confirmed && !_barAlignChecked)
        {
            BarAlignIncomingLocked(to, actions);
            _barAlignChecked = true;
        }

        // The blend anchors on the first downbeat after a CONFIRMED lock — beat-locked, bar-aligned,
        // bar-anchored. This is the "no room for error" gate: an unlocked pairing never becomes audible.
        if (confirmed && _barAlignChecked && downbeat)
        {
            _startBeat = beatNow;
            _lastProgress = 0.0;
            _phase = AutomixPhase.Transitioning;
            _logger.LogInformation("Auto-mix transition started at beat {Beat:F2}.", beatNow);
            return true;
        }

        if (beatNow - _syncStartBeat > _settings.SyncTimeoutBars * _settings.BeatsPerBar)
        {
            AbortLocked(_startedIncoming, "incoming deck did not beat-lock in time", actions);
            return true;
        }
        return false;
    }

    private bool TickTransitioningLocked(double beatNow, List<PerformanceAction> actions)
    {
        double progress = Math.Clamp((beatNow - _startBeat) / _shape!.BeatsTotal, 0.0, 1.0);
        _lastProgress = progress;
        DiffFrameLocked(_profile!.Evaluate(progress, _shape), actions);

        if (progress >= 1.0)
        {
            FinishLocked(actions);
            return true;
        }
        return false;
    }

    // ----- completion / abort -----

    private void FinishLocked(List<PerformanceAction> actions)
    {
        int fromSlot = _plan!.FromSlot;
        int toSlot = _plan.ToSlot;

        // Land the floor on the incoming deck, retire the outgoing one (paused, position kept for
        // recovery), release the SYNC latch (the matched rate is retained by the engine's release
        // path), and hand the outgoing channel strip back exactly as the performer had it.
        actions.Add(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, _shape!.ToSide, Slot: 0, Origin: OriginTag));
        if (_decks.ReadDeck(fromSlot).IsPlaying)
            actions.Add(Deck(PerformanceActionKind.DeckPlayPause, fromSlot));
        if (_decks.ReadDeck(toSlot).SyncLocked)
            actions.Add(Deck(PerformanceActionKind.DeckSyncToggle, toSlot));
        RestoreFromChannelLocked(actions);

        _logger.LogInformation("Auto-mix completed: deck {From} → deck {To}.", fromSlot, toSlot);
        ResetLocked();
    }

    private void AbortLocked(bool stopIncoming, string reason, List<PerformanceAction> actions)
    {
        _logger.LogWarning("Auto-mix aborted in {Phase}: {Reason}.", _phase, reason);

        if (stopIncoming && _plan is not null)
        {
            // Silent-preparation failure (e.g. lock timeout): retire the deck WE started; the floor
            // never heard it. The outgoing deck is untouched on every abort path.
            AutomixDeckSnapshot to = _decks.ReadDeck(_plan.ToSlot);
            if (to.IsPlaying)
                actions.Add(Deck(PerformanceActionKind.DeckPlayPause, _plan.ToSlot));
            if (to.SyncLocked)
                actions.Add(Deck(PerformanceActionKind.DeckSyncToggle, _plan.ToSlot));
            RestoreFromChannelLocked(actions);
        }
        // Performer takeover (stopIncoming = false): freeze everything exactly where it is — no
        // snap-back, nothing released. The human inherits a beat-locked incoming deck (safest state).

        ResetLocked();
    }

    private void RestoreFromChannelLocked(List<PerformanceAction> actions)
    {
        if (_fromRestore is not { } restore || _plan is null)
            return;
        int slot = _plan.FromSlot;
        actions.Add(Eq(slot, Mixer.EqBand.Low, restore.Eq.Low));
        actions.Add(Eq(slot, Mixer.EqBand.Mid, restore.Eq.Mid));
        actions.Add(Eq(slot, Mixer.EqBand.High, restore.Eq.High));
        actions.Add(new PerformanceAction(
            PerformanceActionKind.MixerFilter, ActionInputMode.Absolute, restore.Filter, slot, Origin: OriginTag));
    }

    private void ResetLocked()
    {
        _phase = AutomixPhase.Idle;
        _plan = null;
        _profile = null;
        _shape = null;
        _fromRestore = null;
        _lastFrame = null;
        _lockedTicks = 0;
        _barAlignChecked = false;
        _startedIncoming = false;
        _ticksInPhase = 0;
        _lastProgress = 0.0;
    }

    // ----- alignment / dispatch plumbing -----

    // One inaudible, pre-fade correction onto the leader's BAR grid (advisor S2): continuous sync
    // holds beats, but a beat-locked deck can still sit a whole beat off within the bar — fatal for
    // a quantized bass swap. Skipped without a grid (CrossFade degraded mode tolerates it).
    private void BarAlignIncomingLocked(AutomixDeckSnapshot to, List<PerformanceAction> actions)
    {
        AutomixDeckSnapshot from = _decks.ReadDeck(_plan!.FromSlot);
        if (!from.HasGrid || !to.HasGrid || to.LengthSeconds <= 0.0)
            return;

        var follower = new DeckPhase(to.PositionSeconds, to.FirstBeatSeconds, to.BaseBpm);
        var leader = new DeckPhase(from.PositionSeconds, from.FirstBeatSeconds, from.BaseBpm);
        double nudgeSeconds = PhaseAlignmentCalculator.BarPhaseNudgeSeconds(follower, leader, _settings.BeatsPerBar);

        double leaderBpm = from.EffectiveBpm > 0.0 ? from.EffectiveBpm : from.BaseBpm;
        double leaderBeatSeconds = 60.0 / leaderBpm;
        if (Math.Abs(nudgeSeconds) <= 0.6 * leaderBeatSeconds)
            return; // already on the right beat of the bar; the beat lock holds it there

        actions.Add(Deck(PerformanceActionKind.DeckSeek, _plan.ToSlot,
            ActionInputMode.Relative, nudgeSeconds / to.LengthSeconds));
        _logger.LogInformation("Auto-mix bar-aligned the incoming deck by {Nudge:F3}s.", nudgeSeconds);
    }

    // Emits only the parameters this frame actually changes (epsilon-gated) — at a 10 ms tick the
    // action stream stays lean and feedback subscribers are not flooded.
    private void DiffFrameLocked(AutomixFrame frame, List<PerformanceAction> actions)
    {
        int from = _plan!.FromSlot;
        int to = _plan.ToSlot;
        AutomixFrame? last = _lastFrame;

        AddIfChanged(frame.Crossfader, last?.Crossfader, v => new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, v, Slot: 0, Origin: OriginTag), actions);
        AddIfChanged(frame.FromLow, last?.FromLow, v => Eq(from, Mixer.EqBand.Low, v), actions);
        AddIfChanged(frame.FromMid, last?.FromMid, v => Eq(from, Mixer.EqBand.Mid, v), actions);
        AddIfChanged(frame.FromHigh, last?.FromHigh, v => Eq(from, Mixer.EqBand.High, v), actions);
        AddIfChanged(frame.FromFilter, last?.FromFilter, v => new PerformanceAction(
            PerformanceActionKind.MixerFilter, ActionInputMode.Absolute, v, from, Origin: OriginTag), actions);
        AddIfChanged(frame.ToLow, last?.ToLow, v => Eq(to, Mixer.EqBand.Low, v), actions);
        AddIfChanged(frame.ToMid, last?.ToMid, v => Eq(to, Mixer.EqBand.Mid, v), actions);
        AddIfChanged(frame.ToHigh, last?.ToHigh, v => Eq(to, Mixer.EqBand.High, v), actions);
        AddIfChanged(frame.ToFilter, last?.ToFilter, v => new PerformanceAction(
            PerformanceActionKind.MixerFilter, ActionInputMode.Absolute, v, to, Origin: OriginTag), actions);

        _lastFrame = frame;
    }

    private void AddIfChanged(
        double? value, double? previous, Func<double, PerformanceAction> build, List<PerformanceAction> actions)
    {
        if (value is not { } v)
            return;
        if (previous is { } prev && Math.Abs(v - prev) < _settings.DispatchEpsilon)
            return;
        actions.Add(build(v));
    }

    private PerformanceAction Eq(int slot, Mixer.EqBand band, double value)
        => new(PerformanceActionKind.MixerEqBand, ActionInputMode.Absolute, value, slot, band.ToString(),
            Origin: OriginTag);

    private static PerformanceAction Deck(
        PerformanceActionKind kind, int slot, ActionInputMode mode = ActionInputMode.Momentary, double value = 0)
        => new(kind, mode, value, slot, Origin: OriginTag);

    private static IAutomixStyleProfile ProfileFor(AutomixStyle style) => style switch
    {
        AutomixStyle.EqMix => EqMix,
        AutomixStyle.FxMix => FxMix,
        _ => CrossFade,
    };

    private void DispatchAll(List<PerformanceAction> actions)
    {
        if (actions.Count == 0)
            return;
        IPerformanceActionDispatcher? dispatcher;
        lock (_gate) dispatcher = _dispatcher;
        if (dispatcher is null)
            return;
        foreach (PerformanceAction action in actions)
            dispatcher.Dispatch(action);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // The performer-takeover rule: any human gesture (no automix origin) on a control this engine is
    // automating — the mixer, or the transport/tempo of either involved deck — is an instant, silent
    // handover. Our own stamped actions return before the lock, so re-entrancy from Dispatch is safe.
    private void OnActionDispatched(object? sender, PerformanceAction action)
    {
        if (action.Origin == OriginTag)
            return;

        bool aborted = false;
        lock (_gate)
        {
            if (_phase == AutomixPhase.Idle || _plan is null)
                return;
            if (action.Kind == PerformanceActionKind.AutomixToggle)
                return; // the toggle routes through Toggle() itself

            bool mixerTouch = WatchedMixerKinds.Contains(action.Kind);
            bool deckTouch = WatchedDeckKinds.Contains(action.Kind)
                && (action.Slot == _plan.FromSlot || action.Slot == _plan.ToSlot);
            if (!mixerTouch && !deckTouch)
                return;

            AbortLocked(stopIncoming: false, $"performer touched {action.Kind}", new List<PerformanceAction>());
            aborted = true;
        }
        if (aborted)
            RaiseChanged();
    }
}
