using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Automix;

/// <summary>
/// The dispatcher handler that owns the auto-mix actions (doc 04/11): engage/abort, the TIME knob,
/// and the style selector. Thin routing onto <see cref="AutomixController"/> plus feedback — the
/// AUTOMIX button LED/state, the knob position (with the resolved bar count in the argument), and
/// the selected style — so UI and MIDI surfaces follow the same truth.
/// </summary>
public sealed class AutomixActionHandler : PerformanceActionHandlerBase, IDisposable
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds = new HashSet<PerformanceActionKind>
    {
        PerformanceActionKind.AutomixToggle,
        PerformanceActionKind.AutomixSetDuration,
        PerformanceActionKind.AutomixSetStyle,
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
            case PerformanceActionKind.AutomixSetStyle:
                _controller.SetStyle(ParseStyle(action.Argument));
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
        PerformanceActionKind.AutomixSetStyle => new ActionFeedbackState(
            IsActive: false,
            IsAvailable: true,
            Value: 0,
            Argument: _controller.Style.ToString()),
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

    private static AutomixStyle ParseStyle(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument) || !Enum.TryParse(argument, ignoreCase: true, out AutomixStyle style))
            throw new ArgumentException(
                "AutomixSetStyle requires Argument set to a style name (CrossFade/EqMix/FxMix).", nameof(argument));
        return style;
    }

    private void OnControllerChanged(object? sender, EventArgs e)
    {
        try
        {
            RaiseFeedback(PerformanceActionKind.AutomixToggle, 0,
                GetFeedback(PerformanceActionKind.AutomixToggle, 0));
            RaiseFeedback(PerformanceActionKind.AutomixSetDuration, 0,
                GetFeedback(PerformanceActionKind.AutomixSetDuration, 0));
            RaiseFeedback(PerformanceActionKind.AutomixSetStyle, 0,
                GetFeedback(PerformanceActionKind.AutomixSetStyle, 0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-mix feedback publication failed; state remains consistent.");
        }
    }
}
