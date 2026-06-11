using Liveolator.Core.Actions;

namespace Liveolator.App.Features.Mappings;

public sealed class LearningPerformanceActionDispatcher : IPerformanceActionDispatcher
{
    private readonly IPerformanceActionDispatcher _inner;
    private readonly GlobalMidiLearnCoordinator _learn;

    public LearningPerformanceActionDispatcher(
        IPerformanceActionDispatcher inner,
        GlobalMidiLearnCoordinator learn)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _learn = learn ?? throw new ArgumentNullException(nameof(learn));
    }

    public event EventHandler<ActionFeedbackChanged>? FeedbackChanged
    {
        add => _inner.FeedbackChanged += value;
        remove => _inner.FeedbackChanged -= value;
    }

    public event EventHandler<PerformanceAction>? ActionDispatched
    {
        add => _inner.ActionDispatched += value;
        remove => _inner.ActionDispatched -= value;
    }

    public void Dispatch(PerformanceAction action)
    {
        if (!_learn.TryCaptureUiAction(action))
            _inner.Dispatch(action);
    }

    public ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot = 0)
        => _inner.GetFeedback(kind, slot);
}
