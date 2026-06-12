using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Automix;

/// <summary>
/// The dispatcher handler that owns the auto-mix actions (doc 04/11): engage/abort and the TIME
/// knob. Thin routing onto <see cref="AutomixController"/> plus feedback — the AUTOMIX button
/// LED/state and the knob position (with the resolved bar count in the argument) — so UI and MIDI
/// surfaces follow the same truth.
/// </summary>
public sealed class AutomixActionHandler : PerformanceActionHandlerBase, IDisposable
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.AutomixToggle,
        PerformanceActionKind.AutomixSetDuration,
    };

    private readonly AutomixController _controller;
    private readonly ILogger _logger;
    private bool _disposed;

    public AutomixActionHandler(AutomixController controller, ILoggerFactory? loggerFactory = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<AutomixActionHandler>();
        _controller.Changed += OnControllerChanged;
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action.Kind)
        {
            case PerformanceActionKind.AutomixToggle:
                _controller.Toggle();
                break;
            case PerformanceActionKind.AutomixSetDuration:
                _controller.SetDurationKnob(action.Value);
                break;
            default:
                break; // dispatcher guarantees only handled kinds reach here
        }
    }

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot) => kind switch
    {
        PerformanceActionKind.AutomixToggle => new ActionFeedbackState(
            IsActive: _controller.Phase != AutomixPhase.Idle,
            IsAvailable: true,
            Value: _controller.Progress,
            Argument: StatusText()),
        PerformanceActionKind.AutomixSetDuration => new ActionFeedbackState(
            IsActive: false,
            IsAvailable: true,
            Value: _controller.DurationKnob,
            Argument: _controller.RequestedBars.ToString()),
        _ => ActionFeedbackState.Unavailable,
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _controller.Changed -= OnControllerChanged;
    }

    // The button surfaces WHY it refused (e.g. "TempoGapTooLarge") until the next state change —
    // no silent failure (global standard #26).
    private string StatusText()
    {
        AutomixPhase phase = _controller.Phase;
        if (phase == AutomixPhase.Idle && _controller.LastRefusal != AutomixRefusal.None)
            return _controller.LastRefusal.ToString();
        return phase.ToString();
    }

    private void OnControllerChanged(object? sender, EventArgs e)
    {
        try
        {
            RaiseFeedback(PerformanceActionKind.AutomixToggle, 0,
                GetFeedback(PerformanceActionKind.AutomixToggle, 0));
            RaiseFeedback(PerformanceActionKind.AutomixSetDuration, 0,
                GetFeedback(PerformanceActionKind.AutomixSetDuration, 0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-mix feedback publication failed; state remains consistent.");
        }
    }
}
