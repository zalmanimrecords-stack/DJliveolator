namespace Liveolator.Core.Actions;

/// <summary>
/// Owns one concern's actions (transport, beat, visual, deck, mixer, playlist, …) and applies
/// them to its engine. The dispatcher routes each <see cref="PerformanceActionKind"/> to the
/// single handler that declares it in <see cref="HandledKinds"/>, so adding a controller never
/// touches engine code and each handler stays a small single-responsibility unit (doc 04).
/// </summary>
public interface IPerformanceActionHandler
{
    /// <summary>The kinds this handler owns. Each kind must be owned by exactly one handler.</summary>
    IReadOnlySet<PerformanceActionKind> HandledKinds { get; }

    /// <summary>Applies <paramref name="action"/> to the underlying engine.</summary>
    void Handle(PerformanceAction action);

    /// <summary>Reports the current feedback state of one of this handler's kinds.</summary>
    ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot);

    /// <summary>Raised by the handler when one of its actions changes feedback state.</summary>
    event EventHandler<ActionFeedbackChanged>? FeedbackChanged;
}
