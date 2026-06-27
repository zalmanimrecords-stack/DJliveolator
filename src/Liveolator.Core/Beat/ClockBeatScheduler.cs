namespace Liveolator.Core.Beat;

/// <summary>
/// Clock-driven <see cref="IBeatScheduler"/>: defers an action to the next beat/bar boundary on the
/// one shared <see cref="IBeatClock"/>, so visual launches and playlist edits land on the same grid as
/// the audio (doc 03/04/08). It listens to the clock's boundary flags (<see cref="BeatClockState.IsBeat"/>
/// / <see cref="BeatClockState.IsDownbeat"/>) rather than re-deriving host-time, keeping the grid a
/// single source of truth. Replaces the interim immediate-fire scheduler.
/// </summary>
/// <remarks>
/// When the grid is not trustworthy at schedule time (confidence below the floor, or no tempo yet) it
/// fires immediately rather than snapping to a shaky grid — the same guard <see cref="QuantizedLaunch"/>
/// applies. <see cref="Schedule"/> is called from the dispatcher thread while the clock raises
/// <c>StateChanged</c> on its pump thread, so the pending list is lock-guarded; callbacks fire outside
/// the lock so a re-scheduling callback can't deadlock.
/// </remarks>
public sealed class ClockBeatScheduler : IBeatScheduler, IDisposable
{
    private sealed record Pending(Quantize When, int EveryN, Action OnFire);

    private readonly IBeatClock _clock;
    private readonly double _minConfidence;
    private readonly object _gate = new();
    private readonly List<Pending> _pending = new();

    public ClockBeatScheduler(IBeatClock clock, double minConfidence = QuantizedLaunch.DefaultMinConfidence)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _minConfidence = minConfidence;
        _clock.StateChanged += OnStateChanged;
    }

    public void Schedule(Quantize when, int everyN, Action onFire)
    {
        ArgumentNullException.ThrowIfNull(onFire);

        BeatClockState state = _clock.Current;
        if (when == Quantize.Immediate || state.Confidence < _minConfidence || state.Bpm <= 0)
        {
            onFire();
            return;
        }

        lock (_gate)
            _pending.Add(new Pending(when, Math.Max(1, everyN), onFire));
    }

    private void OnStateChanged(object? sender, BeatClockState state)
    {
        List<Action>? due = null;
        lock (_gate)
        {
            for (int i = 0; i < _pending.Count;)
            {
                if (IsDue(_pending[i], state))
                {
                    (due ??= new List<Action>()).Add(_pending[i].OnFire);
                    _pending.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
        }

        if (due is null)
            return;
        foreach (Action onFire in due)
            onFire();
    }

    private static bool IsDue(Pending pending, BeatClockState state) => pending.When switch
    {
        Quantize.NextBeat => state.IsBeat,
        Quantize.NextBar => state.IsDownbeat,
        Quantize.EveryNBars => state.IsDownbeat && state.BarNumber % pending.EveryN == 0,
        _ => true,
    };

    public void Dispose() => _clock.StateChanged -= OnStateChanged;
}
