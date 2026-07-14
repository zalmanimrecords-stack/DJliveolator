using Liveolator.Core.Actions;

namespace Liveolator.Core.Beat;

/// <summary>
/// The dispatcher handler that owns the beat actions, translating them into clock-control calls
/// (doc 04). It stamps each time-sensitive action with the host clock's "now" and reports the lock
/// state back as feedback so a Push/CMD lock LED can follow it (doc 05/06). This is the first real
/// engine handler wired into the action layer.
/// </summary>
public sealed class BeatActionHandler : PerformanceActionHandlerBase
{
    /// <summary>Default phase-nudge size, in beats (a fine micro-adjustment).</summary>
    public const double DefaultNudgeBeats = 0.01;

    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.BeatTapTempo,
        PerformanceActionKind.BeatLock,
        PerformanceActionKind.BeatUnlock,
        PerformanceActionKind.BeatHalfTempo,
        PerformanceActionKind.BeatDoubleTempo,
        PerformanceActionKind.BeatNudgeForward,
        PerformanceActionKind.BeatNudgeBackward,
        PerformanceActionKind.BeatResetGrid,
        PerformanceActionKind.BeatSetDownbeat,
    };

    private readonly IBeatClockControl _clock;
    private readonly IHostClock _hostClock;
    private readonly double _nudgeBeats;

    public BeatActionHandler(IBeatClockControl clock, IHostClock hostClock, double nudgeBeats = DefaultNudgeBeats)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _hostClock = hostClock ?? throw new ArgumentNullException(nameof(hostClock));
        if (nudgeBeats <= 0)
            throw new ArgumentOutOfRangeException(nameof(nudgeBeats), nudgeBeats, "Nudge size must be positive.");
        _nudgeBeats = nudgeBeats;
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        long now = _hostClock.NowTicks;

        switch (action.Kind)
        {
            case PerformanceActionKind.BeatTapTempo:
                _clock.Tap(now);
                break;
            case PerformanceActionKind.BeatLock:
                _clock.Lock();
                RaiseLockFeedback();
                break;
            case PerformanceActionKind.BeatUnlock:
                _clock.Unlock();
                RaiseLockFeedback();
                break;
            case PerformanceActionKind.BeatHalfTempo:
                _clock.HalfTempo(now);
                break;
            case PerformanceActionKind.BeatDoubleTempo:
                _clock.DoubleTempo(now);
                break;
            case PerformanceActionKind.BeatNudgeForward:
                _clock.Nudge(ResolveNudge(action, direction: 1), now);
                break;
            case PerformanceActionKind.BeatNudgeBackward:
                _clock.Nudge(ResolveNudge(action, direction: -1), now);
                break;
            case PerformanceActionKind.BeatResetGrid:
            case PerformanceActionKind.BeatSetDownbeat:
                _clock.SetDownbeat(now);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    private double ResolveNudge(PerformanceAction action, int direction)
    {
        double steps = action.InputMode == ActionInputMode.Relative ? action.Value : 1;
        return direction * steps * _nudgeBeats;
    }

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
        => kind == PerformanceActionKind.BeatLock
            ? new ActionFeedbackState(IsActive: _clock.IsLocked, IsAvailable: true, Value: 0)
            : ActionFeedbackState.Unavailable;

    private void RaiseLockFeedback()
        => RaiseFeedback(
            PerformanceActionKind.BeatLock, slot: 0,
            new ActionFeedbackState(IsActive: _clock.IsLocked, IsAvailable: true, Value: 0));
}
