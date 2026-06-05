namespace Liveolator.Core.Beat;

/// <summary>
/// A beat clock driven purely by performer tap/lock/nudge with no audio analysis
/// (<see cref="BeatClockSource.Manual"/>, explicitly buildable without an audio source per doc 03).
/// Holds an immutable <see cref="BeatTimeline"/> and republishes an immutable
/// <see cref="BeatClockState"/> on each change. Times are supplied by the caller, so it is fully
/// deterministic and unit-testable.
/// </summary>
public sealed class ManualBeatClock : IBeatClock, IBeatClockControl
{
    private readonly long _ticksPerSecond;
    private readonly int _beatsPerBar;
    private readonly TapTempoService _tapTempo;

    private BeatTimeline? _timeline;
    private double _bpm;
    private bool _locked;
    private long _lastHostTime;
    private int _lastBeatIndex = int.MinValue;
    private int _lastBarIndex = int.MinValue;

    /// <param name="ticksPerSecond">Resolution of the host times supplied to the clock.</param>
    /// <param name="beatsPerBar">Beats per bar (4 = 4/4); must be positive.</param>
    public ManualBeatClock(long ticksPerSecond, int beatsPerBar = BeatQuantizer.DefaultBeatsPerBar)
    {
        if (ticksPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), ticksPerSecond, "Tick rate must be positive.");
        if (beatsPerBar <= 0)
            throw new ArgumentOutOfRangeException(nameof(beatsPerBar), beatsPerBar, "Beats per bar must be positive.");

        _ticksPerSecond = ticksPerSecond;
        _beatsPerBar = beatsPerBar;
        _tapTempo = new TapTempoService(ticksPerSecond);
        Current = BeatClockState.Idle;
    }

    /// <inheritdoc />
    public BeatClockState Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler<BeatClockState>? StateChanged;

    /// <inheritdoc />
    public bool IsLocked => _locked;

    /// <summary>The current tempo, or 0 before one is established.</summary>
    public double Bpm => _bpm;

    /// <summary>Recomputes and republishes state at <paramref name="hostTimeTicks"/> (the host render loop entry point).</summary>
    public void Update(long hostTimeTicks)
    {
        if (_timeline is not null)
            Publish(hostTimeTicks);
    }

    /// <inheritdoc />
    public void Tap(long hostTimeTicks)
    {
        _tapTempo.Tap(hostTimeTicks);
        if (!_tapTempo.TryGetBpm(out double bpm))
            return; // need at least two taps

        if (!_locked)
            _bpm = bpm; // locked: follow phase from taps but keep the frozen tempo
        if (_bpm <= 0)
            return;

        // Treat the latest tap as a downbeat: re-establish the grid origin here.
        _timeline = new BeatTimeline(_bpm, anchorBeat: 0, hostTimeTicks, _ticksPerSecond);
        ResetCrossings();
        Publish(hostTimeTicks, forceBeat: true);
    }

    /// <inheritdoc />
    public void Lock()
    {
        _locked = true;
        RepublishFlags();
    }

    /// <inheritdoc />
    public void Unlock()
    {
        _locked = false;
        RepublishFlags();
    }

    /// <inheritdoc />
    public void HalfTempo(long hostTimeTicks) => ScaleTempo(0.5, hostTimeTicks);

    /// <inheritdoc />
    public void DoubleTempo(long hostTimeTicks) => ScaleTempo(2.0, hostTimeTicks);

    /// <inheritdoc />
    public void Nudge(double beatDelta, long hostTimeTicks)
    {
        if (_timeline is null)
            return;

        double currentBeat = _timeline.BeatAtTime(hostTimeTicks);
        _timeline = new BeatTimeline(_bpm, currentBeat + beatDelta, hostTimeTicks, _ticksPerSecond);
        Publish(hostTimeTicks);
    }

    /// <inheritdoc />
    public void SetDownbeat(long hostTimeTicks)
    {
        if (_bpm <= 0)
            return;

        _timeline = new BeatTimeline(_bpm, anchorBeat: 0, hostTimeTicks, _ticksPerSecond);
        ResetCrossings();
        Publish(hostTimeTicks, forceBeat: true);
    }

    // Half/double are deliberate performer commands, so they change tempo even while locked.
    private void ScaleTempo(double factor, long hostTimeTicks)
    {
        if (_timeline is null || _bpm <= 0)
            return;

        double currentBeat = _timeline.BeatAtTime(hostTimeTicks);
        _bpm *= factor;
        _timeline = new BeatTimeline(_bpm, currentBeat, hostTimeTicks, _ticksPerSecond);
        Publish(hostTimeTicks);
    }

    private void ResetCrossings()
    {
        _lastBeatIndex = int.MinValue;
        _lastBarIndex = int.MinValue;
    }

    private void RepublishFlags()
    {
        if (_timeline is null)
        {
            Current = Current with { IsLocked = _locked };
            StateChanged?.Invoke(this, Current);
            return;
        }

        Publish(_lastHostTime);
    }

    private void Publish(long hostTimeTicks, bool forceBeat = false)
    {
        _lastHostTime = hostTimeTicks;

        double beat = _timeline!.BeatAtTime(hostTimeTicks);
        int beatIndex = (int)Math.Floor(beat);
        int barIndex = (int)Math.Floor(beat / _beatsPerBar);

        bool isBeat = forceBeat || beatIndex != _lastBeatIndex;
        bool isDownbeat = forceBeat || barIndex != _lastBarIndex;
        _lastBeatIndex = beatIndex;
        _lastBarIndex = barIndex;

        Current = new BeatClockState(
            Bpm: _bpm,
            Confidence: _bpm > 0 ? 1.0 : 0.0, // performer-driven tempo is certain
            BeatPhase: beat - Math.Floor(beat),
            BarPhase: _timeline.PhaseAtTime(hostTimeTicks, _beatsPerBar),
            BeatCount: beatIndex,
            BarNumber: barIndex,
            IsBeat: isBeat,
            IsDownbeat: isDownbeat,
            IsLocked: _locked,
            Source: BeatClockSource.Manual,
            Candidates: Array.Empty<TempoCandidate>());

        StateChanged?.Invoke(this, Current);
    }
}
