using Liveolator.Core.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Platform;

/// <summary>
/// The dispatcher handler that owns <see cref="PerformanceActionKind.SystemMasterVolume"/> (doc 04): it
/// drives the computer's OS master volume through the <see cref="ISystemVolumeController"/> seam, so the
/// UI knob, a MIDI controller, and autopilot all set the system volume through the one action layer.
/// Pure managed; unit-tests with a fake controller.
/// </summary>
public sealed class SystemVolumeActionHandler : PerformanceActionHandlerBase
{
    private static readonly IReadOnlySet<PerformanceActionKind> Kinds =
        new HashSet<PerformanceActionKind> { PerformanceActionKind.SystemMasterVolume };

    private readonly ISystemVolumeController _controller;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public SystemVolumeActionHandler(ISystemVolumeController controller, ILoggerFactory? loggerFactory = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SystemVolumeActionHandler>();
    }

    /// <inheritdoc />
    public override IReadOnlySet<PerformanceActionKind> HandledKinds => Kinds;

    /// <inheritdoc />
    public override void Handle(PerformanceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Kind != PerformanceActionKind.SystemMasterVolume)
            return; // dispatcher guarantees only handled kinds reach here

        if (!_controller.IsAvailable)
        {
            // Unsupported host: report unavailable so a controller LED / the UI reflects "no target".
            RaiseFeedback(PerformanceActionKind.SystemMasterVolume, slot: 0, ActionFeedbackState.Unavailable);
            return;
        }

        double level;
        lock (_gate)
        {
            double target = action.InputMode == ActionInputMode.Relative
                ? _controller.GetVolume() + action.Value
                : action.Value;
            level = Math.Clamp(target, 0.0, 1.0);

            try
            {
                _controller.SetVolume(level);
            }
            catch (Exception ex)
            {
                // A volume change must never take down a live performance; log and surface as available
                // at the last-known level rather than throwing (global standards #16/#26).
                _logger.LogWarning(ex, "Failed to set OS master volume to {Level}", level);
                level = _controller.GetVolume();
            }
        }

        RaiseFeedback(
            PerformanceActionKind.SystemMasterVolume, slot: 0,
            new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: level));
        _logger.LogDebug("OS master volume set to {Level}", level);
    }

    /// <inheritdoc />
    public override ActionFeedbackState GetFeedback(PerformanceActionKind kind, int slot)
    {
        if (kind != PerformanceActionKind.SystemMasterVolume || !_controller.IsAvailable)
            return ActionFeedbackState.Unavailable;
        return new ActionFeedbackState(IsActive: false, IsAvailable: true, Value: _controller.GetVolume());
    }
}
