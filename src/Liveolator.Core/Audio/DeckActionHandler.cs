using Liveolator.Core.Actions;

namespace Liveolator.Core.Audio;

/// <summary>
/// The dispatcher handler that owns deck transport actions (doc 04/11): it translates
/// load/play-pause/stop intents into <see cref="IAudioPlaybackEngine"/> calls, so the UI, a
/// controller, or autopilot all drive playback through the one action layer rather than touching
/// the engine directly. Reports play state back as feedback for a play LED/indicator.
/// </summary>
public sealed class DeckActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.DeckLoadTrack,
        PerformanceActionKind.DeckPlayPause,
        PerformanceActionKind.TransportStop,
    };

    private readonly IAudioPlaybackEngine _engine;

    public DeckActionHandler(IAudioPlaybackEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action.Kind)
        {
            case PerformanceActionKind.DeckLoadTrack:
                if (string.IsNullOrWhiteSpace(action.Argument))
                    throw new ArgumentException("DeckLoadTrack requires Argument set to the track path.", nameof(action));
                _engine.Load(action.Argument);
                break;
            case PerformanceActionKind.DeckPlayPause:
                _engine.PlayPause();
                RaisePlayFeedback();
                break;
            case PerformanceActionKind.TransportStop:
                _engine.Stop();
                RaisePlayFeedback();
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
        => kind == PerformanceActionKind.DeckPlayPause
            ? new ActionFeedbackState(IsActive: _engine.IsPlaying, IsAvailable: true, Value: 0)
            : ActionFeedbackState.Unavailable;

    private void RaisePlayFeedback()
        => RaiseFeedback(
            PerformanceActionKind.DeckPlayPause, slot: 0,
            new ActionFeedbackState(IsActive: _engine.IsPlaying, IsAvailable: true, Value: 0));
}
