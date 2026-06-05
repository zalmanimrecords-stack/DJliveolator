using Liveolator.Core.Actions;

namespace Liveolator.Core.Audio;

/// <summary>
/// The dispatcher handler that owns deck transport actions (doc 04/11): it translates
/// load/play-pause/stop intents into deck-engine calls, so the UI, a controller, or autopilot all
/// drive playback through the one action layer rather than touching the engine directly. Reports
/// play state back as feedback for a play LED/indicator.
/// </summary>
/// <remarks>
/// Actions are addressed per deck slot via <see cref="PerformanceAction.Slot"/> (A = 0, B = 1).
/// A single-deck engine is adapted to slot 0, so the existing single-deck composition is unchanged;
/// a two-deck engine receives the slot directly.
/// </remarks>
public sealed class DeckActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.DeckLoadTrack,
        PerformanceActionKind.DeckPlayPause,
        PerformanceActionKind.TransportStop,
    };

    private readonly IMultiDeckPlaybackEngine _engine;

    /// <summary>Wraps a single-deck engine (slot 0 only) — the existing composition.</summary>
    public DeckActionHandler(IAudioPlaybackEngine engine)
        : this(new SingleDeckEngineAdapter(engine ?? throw new ArgumentNullException(nameof(engine))))
    {
    }

    /// <summary>Drives a two-deck engine directly, addressing decks by action slot.</summary>
    public DeckActionHandler(IMultiDeckPlaybackEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        int slot = ValidateSlot(action.Slot);

        switch (action.Kind)
        {
            case PerformanceActionKind.DeckLoadTrack:
                if (string.IsNullOrWhiteSpace(action.Argument))
                    throw new ArgumentException("DeckLoadTrack requires Argument set to the track path.", nameof(action));
                _engine.Load(slot, action.Argument);
                break;
            case PerformanceActionKind.DeckPlayPause:
                _engine.PlayPause(slot);
                RaisePlayFeedback(slot);
                break;
            case PerformanceActionKind.TransportStop:
                _engine.Stop(slot);
                RaisePlayFeedback(slot);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
        => kind == PerformanceActionKind.DeckPlayPause && slot >= 0 && slot < _engine.DeckCount
            ? new ActionFeedbackState(IsActive: _engine.IsPlaying(slot), IsAvailable: true, Value: 0)
            : ActionFeedbackState.Unavailable;

    private void RaisePlayFeedback(int slot)
        => RaiseFeedback(
            PerformanceActionKind.DeckPlayPause, slot,
            new ActionFeedbackState(IsActive: _engine.IsPlaying(slot), IsAvailable: true, Value: 0));

    private int ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= _engine.DeckCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Deck slot is out of range for this engine.");
        return slot;
    }
}
