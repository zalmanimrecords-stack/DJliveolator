namespace Liveolator.Core.Actions;

/// <summary>
/// Marshals feedback notifications onto the thread subscribers expect (the UI thread in the
/// app). The dispatcher posts every re-raised <see cref="ActionFeedbackChanged"/> through this
/// seam so handlers and callers stay thread-agnostic (doc 04) and Core stays UI-free — the app
/// supplies an Avalonia-backed implementation, tests use the inline default.
/// </summary>
public interface IActionFeedbackSynchronizer
{
    /// <summary>Runs <paramref name="work"/> on the subscriber-facing thread.</summary>
    void Post(Action work);
}
