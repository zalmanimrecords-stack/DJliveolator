using Liveolator.App.Shell;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// A single normalized 0..1 control (knob / fader / ring) backing a mixer gain, crossfader, EQ band,
/// filter, or visual macro. A user-driven <see cref="Value"/> change emits an Absolute action via the
/// supplied callback (the doc 04 seam); a value pushed back from dispatcher feedback uses
/// <see cref="SetFromFeedback"/>, which updates the bound control WITHOUT re-emitting — so a controller
/// move or an echoed feedback event does not loop. A null callback means "no backend yet": the control
/// is disabled and never emits.
/// </summary>
public sealed class ContinuousControlViewModel : ViewModelBase
{
    private readonly Action<double>? _onUserChanged;
    private double _value;

    /// <param name="label">Short control label (e.g. "Hi", "A", "Intensity").</param>
    /// <param name="initial">Starting normalized value, 0..1.</param>
    /// <param name="onUserChanged">Invoked on a user-driven change; null disables the control.</param>
    public ContinuousControlViewModel(string label, double initial, Action<double>? onUserChanged)
    {
        Label = label;
        _value = initial;
        _onUserChanged = onUserChanged;
        IsEnabled = onUserChanged is not null;
    }

    /// <summary>Short label shown under/next to the control.</summary>
    public string Label { get; }

    /// <summary>True when the control has a backend and can emit; the UI disables it otherwise.</summary>
    public bool IsEnabled { get; }

    /// <summary>The normalized value, two-way bound to the slider/knob. User edits emit an action.</summary>
    public double Value
    {
        get => _value;
        set
        {
            double previous = _value;
            this.RaiseAndSetIfChanged(ref _value, value);
            if (!_value.Equals(previous) && IsEnabled)
                _onUserChanged!(_value);
        }
    }

    /// <summary>
    /// Applies a value reported by dispatcher feedback (a controller moved the same control, or our own
    /// echo). Bypasses the emit path so feedback never re-dispatches — the one-source-of-truth rule (doc 12).
    /// </summary>
    public void SetFromFeedback(double value)
    {
        if (_value.Equals(value))
            return;
        _value = value;
        this.RaisePropertyChanged(nameof(Value));
    }
}
