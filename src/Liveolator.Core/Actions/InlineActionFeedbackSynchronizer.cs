namespace Liveolator.Core.Actions;

/// <summary>
/// Runs feedback work on the calling thread. The default when no UI marshaling is needed
/// (headless engines, MCP server, unit tests).
/// </summary>
public sealed class InlineActionFeedbackSynchronizer : IActionFeedbackSynchronizer
{
    /// <summary>The shared, stateless instance.</summary>
    public static InlineActionFeedbackSynchronizer Instance { get; } = new();

    /// <inheritdoc />
    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work();
    }
}
