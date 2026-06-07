namespace Liveolator.Core.Beat;

/// <summary>
/// The shared beat clock driven by the sync <em>master</em> deck (<see cref="BeatClockSource.Deck"/>) —
/// the product's signature audio↔visual link realised from a deterministic grid rather than re-analysis
/// of the audible mix. When a deck is the sync master, the visuals and the Live beat readout follow its
/// known tempo and beat position directly, so they lock to the music with no detection lag and no drift.
/// Holds an immutable <see cref="BeatTimeline"/> and republishes an immutable <see cref="BeatClockState"/>
/// on each pump, exactly like <see cref="ManualBeatClock"/>; times are supplied by the caller, so it is
/// fully deterministic and unit-testable.
/// </summary>
/// <remarks>
/// The host render loop pumps <see cref="Update(double, double, long)"/> with the master deck's live
/// effective tempo and continuous beat position (both read from real playback). The timeline is
/// re-anchored to that true beat <em>every</em> tick, so it only ever interpolates between samples and
/// is re-pinned to real audio each frame — the clock cannot drift from the music (DRIFT PREVENTION).
/// </remarks>
public sealed class DeckDrivenBeatClock : IBeatClock
{
    private readonly long _ticksPerSecond;
    private readonly int _beatsPerBar;

    private BeatTimeline? _timeline;
    private bool _idle = true;
    private int _lastBeatIndex = int.MinValue;
    private int _lastBarIndex = int.MinValue;

    /// <param name="ticksPerSecond">Resolution of the host times supplied to the clock; must be positive.</param>
    /// <param name="beatsPerBar">Beats per bar (4 = 4/4); must be positive.</param>
    public DeckDrivenBeatClock(long ticksPerSecond, int beatsPerBar = BeatQuantizer.DefaultBeatsPerBar)
    {
        if (ticksPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), ticksPerSecond, "Tick rate must be positive.");
        if (beatsPerBar <= 0)
            throw new ArgumentOutOfRangeException(nameof(beatsPerBar), beatsPerBar, "Beats per bar must be positive.");

        _ticksPerSecond = ticksPerSecond;
        _beatsPerBar = beatsPerBar;
        Current = BeatClockState.Idle;
    }

    /// <inheritdoc />
    public BeatClockState Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler<BeatClockState>? StateChanged;

    /// <summary>
    /// Drive the clock from the master deck's live state. <paramref name="effectiveBpm"/> is the deck's
    /// audible tempo (its base BPM scaled by pitch/sync), and <paramref name="continuousBeat"/> is its
    /// continuous beat position — <c>(positionSeconds − firstBeatSeconds) / (60 / effectiveBpm)</c>. A
    /// non-positive tempo (no master playing or unknown BPM) puts the clock idle.
    /// </summary>
    public void Update(double effectiveBpm, double continuousBeat, long hostTimeTicks)
    {
        if (effectiveBpm <= 0.0 || double.IsNaN(effectiveBpm) || double.IsNaN(continuousBeat))
        {
            GoIdle();
            return;
        }

        _timeline = new BeatTimeline(effectiveBpm, continuousBeat, hostTimeTicks, _ticksPerSecond);
        Publish(effectiveBpm, hostTimeTicks);
    }

    /// <summary>Put the clock idle (no master deck). Publishes the idle state once on the transition.</summary>
    public void Reset() => GoIdle();

    private void GoIdle()
    {
        _timeline = null;
        _lastBeatIndex = int.MinValue;
        _lastBarIndex = int.MinValue;
        if (_idle)
            return; // already idle — don't spam StateChanged every tick the master is stopped

        _idle = true;
        Current = BeatClockState.Idle;
        StateChanged?.Invoke(this, Current);
    }

    private void Publish(double bpm, long hostTimeTicks)
    {
        _idle = false;

        double beat = _timeline!.BeatAtTime(hostTimeTicks);
        int beatIndex = (int)Math.Floor(beat);
        int barIndex = (int)Math.Floor(beat / _beatsPerBar);

        bool isBeat = beatIndex != _lastBeatIndex;
        bool isDownbeat = barIndex != _lastBarIndex;
        _lastBeatIndex = beatIndex;
        _lastBarIndex = barIndex;

        Current = new BeatClockState(
            Bpm: bpm,
            Confidence: 1.0, // the master deck's analyzed tempo is the reference — certain by definition
            BeatPhase: beat - Math.Floor(beat),
            BarPhase: _timeline.PhaseAtTime(hostTimeTicks, _beatsPerBar),
            BeatCount: beatIndex,
            BarNumber: barIndex,
            IsBeat: isBeat,
            IsDownbeat: isDownbeat,
            IsLocked: true, // a deck-driven grid is locked to playback (not a free-running tap tempo)
            Source: BeatClockSource.Deck,
            Candidates: Array.Empty<TempoCandidate>());

        StateChanged?.Invoke(this, Current);
    }
}
