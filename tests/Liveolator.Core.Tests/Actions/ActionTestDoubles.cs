using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;

namespace Liveolator.Core.Tests.Actions;

/// <summary>
/// A handler standing in for a real engine: records the actions it receives, can be told to
/// throw, returns a canned feedback state, and exposes <see cref="Raise"/> so tests can drive
/// the feedback pipeline without a real engine.
/// </summary>
internal sealed class FakeActionHandler : PerformanceActionHandlerBase
{
    private readonly HashSet<PerformanceActionKind> _kinds;

    public FakeActionHandler(params PerformanceActionKind[] kinds) => _kinds = new HashSet<PerformanceActionKind>(kinds);

    public override IReadOnlySet<PerformanceActionKind> HandledKinds => _kinds;

    public List<PerformanceAction> Handled { get; } = new();

    public bool ThrowOnHandle { get; set; }

    public bool ThrowOnGetFeedback { get; set; }

    public ActionFeedbackState FeedbackToReturn { get; set; } = ActionFeedbackState.Unavailable;

    public override void Handle(PerformanceAction action)
    {
        Handled.Add(action);
        if (ThrowOnHandle)
            throw new InvalidOperationException("engine boom");
    }

    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
    {
        if (ThrowOnGetFeedback)
            throw new InvalidOperationException("feedback boom");
        return FeedbackToReturn;
    }

    /// <summary>Emits a feedback change, as a real handler would when its engine state moves.</summary>
    public void Raise(PerformanceActionKind kind, int slot, ActionFeedbackState state)
        => RaiseFeedback(kind, slot, state);
}

/// <summary>Records log entries so tests can assert that failures are surfaced, not swallowed.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
