using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Recording;

/// <summary>
/// The dispatcher handler that owns <see cref="PerformanceActionKind.MasterRecordToggle"/> (doc 04,
/// roadmap X2): it toggles master-mix capture through the <see cref="IMasterRecorder"/> seam so the REC
/// button, a controller pad, and autopilot all start/stop recording through the one action layer. Pure
/// managed; unit-tests with a fake recorder.
///
/// The toggle decision reads the recorder's own <see cref="IMasterRecorder.IsRecording"/> truth rather
/// than a local latch, so a failed start (IO error) never leaves the handler out of sync with reality.
/// Feedback mirrors the recorder state back so a Push/UI surface follows it without polling.
/// </summary>
public sealed class RecordingActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds =
        new HashSet<PerformanceActionKind> { PerformanceActionKind.MasterRecordToggle };

    private readonly IMasterRecorder _recorder;
    private readonly IRecordingPathProvider _paths;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public RecordingActionHandler(
        IMasterRecorder recorder, IRecordingPathProvider paths, ILoggerFactory? loggerFactory = null)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<RecordingActionHandler>();
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Kind != PerformanceActionKind.MasterRecordToggle)
            return; // dispatcher guarantees only handled kinds reach here

        if (!_recorder.IsAvailable)
        {
            // No realtime master tap on this host: surface "no target" so the REC button greys out.
            RaiseFeedback(PerformanceActionKind.MasterRecordToggle, slot: 0, ActionFeedbackState.Unavailable);
            return;
        }

        lock (_gate)
        {
            if (_recorder.IsRecording)
            {
                _recorder.Stop();
                _logger.LogInformation("Master recording stopped.");
            }
            else
            {
                string path = string.IsNullOrWhiteSpace(action.Argument)
                    ? _paths.NextRecordingPath()
                    : action.Argument;
                if (_recorder.Start(path))
                    _logger.LogInformation("Master recording started: {Path}", path);
                else
                    // Start declined (already running, or the impl logged an IO failure). Do not latch on.
                    _logger.LogWarning("Master recording could not start at {Path}.", path);
            }
        }

        RaiseFeedback(PerformanceActionKind.MasterRecordToggle, slot: 0, CurrentState());
    }

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
        => kind == PerformanceActionKind.MasterRecordToggle ? CurrentState() : ActionFeedbackState.Unavailable;

    private ActionFeedbackState CurrentState()
        => _recorder.IsAvailable
            ? new ActionFeedbackState(IsActive: _recorder.IsRecording, IsAvailable: true, Value: 0)
            : ActionFeedbackState.Unavailable;
}
