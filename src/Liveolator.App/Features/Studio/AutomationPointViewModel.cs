using Liveolator.App.Shell;
using Liveolator.Core.Studio;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// One editable keyframe on an automation curve: a time (seconds, ≥ 0) and a control value (0..1).
/// Mutable so the curve editor can drag it; <see cref="ToKeyframe"/> projects it to the immutable
/// Core record.
/// </summary>
public sealed class AutomationPointViewModel : ViewModelBase
{
    private double _timeSeconds;
    private double _value;

    public AutomationPointViewModel(double timeSeconds, double value)
    {
        _timeSeconds = System.Math.Max(0, timeSeconds);
        _value = System.Math.Clamp(value, 0, 1);
    }

    public double TimeSeconds
    {
        get => _timeSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeSeconds, System.Math.Max(0, value));
    }

    public double Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, System.Math.Clamp(value, 0, 1));
    }

    public AutomationKeyframe ToKeyframe() => new(TimeSeconds, Value);
}
