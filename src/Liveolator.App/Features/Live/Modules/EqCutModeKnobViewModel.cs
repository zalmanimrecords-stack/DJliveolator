using System;
using Liveolator.App.Shell;
using Liveolator.Core.Mixer;
using ReactiveUI;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// Backs the mixer-wide EQ cut-depth control as a 3-detent rotary knob (EQ → DEEP → KILL) instead of a
/// cycle button. The knob's normalized 0..1 <see cref="Value"/> is bound two-way to a detented
/// <see cref="Controls.Knob"/>; each detent maps to one <see cref="EqCutMode"/> (EQ at the bottom,
/// KILL at the top — more travel = deeper cut). A user turn to a new detent emits a
/// <see cref="Core.Actions.PerformanceActionKind.MixerEqCutMode"/> with the mode name (absolute select);
/// dispatcher feedback flows back via <see cref="SetFromMode"/> without re-emitting, so a controller move
/// or our own echo never loops. A null callback means "no backend yet": the knob is disabled and inert.
/// </summary>
public sealed class EqCutModeKnobViewModel : ViewModelBase
{
    /// <summary>Number of detents — one per <see cref="EqCutMode"/> value.</summary>
    public const int DetentCount = 3;

    private readonly Action<EqCutMode>? _onUserChanged;
    private double _value;

    /// <param name="initial">The mode the knob starts at (seeded from dispatcher feedback).</param>
    /// <param name="onUserChanged">Invoked when a user turn lands on a new mode; null disables the knob.</param>
    public EqCutModeKnobViewModel(EqCutMode initial, Action<EqCutMode>? onUserChanged)
    {
        _onUserChanged = onUserChanged;
        IsEnabled = onUserChanged is not null;
        _value = ToValue(initial);
    }

    /// <summary>True when the knob has a backend and can emit; the UI disables it otherwise.</summary>
    public bool IsEnabled { get; }

    /// <summary>Normalized 0..1 position, two-way bound to the detented knob. A user edit that crosses
    /// into a new detent emits the corresponding <see cref="EqCutMode"/>.</summary>
    public double Value
    {
        get => _value;
        set
        {
            double snapped = Snap(value);
            EqCutMode previousMode = ToMode(_value);
            this.RaiseAndSetIfChanged(ref _value, snapped);
            this.RaisePropertyChanged(nameof(ModeLabel));
            EqCutMode mode = ToMode(snapped);
            if (mode != previousMode && IsEnabled)
                _onUserChanged!(mode);
        }
    }

    /// <summary>Short uppercase label of the active mode for the knob caption ("EQ"/"DEEP"/"KILL").</summary>
    public string ModeLabel => ToMode(_value).Label();

    /// <summary>The active mode derived from the current detent.</summary>
    public EqCutMode Mode => ToMode(_value);

    /// <summary>
    /// Applies a mode reported by dispatcher feedback (a controller turned it, or our own echo). Bypasses
    /// the emit path so feedback never re-dispatches — the one-source-of-truth rule (doc 12).
    /// </summary>
    public void SetFromMode(EqCutMode mode)
    {
        double next = ToValue(mode);
        if (_value.Equals(next))
            return;
        _value = next;
        this.RaisePropertyChanged(nameof(Value));
        this.RaisePropertyChanged(nameof(ModeLabel));
        this.RaisePropertyChanged(nameof(Mode));
    }

    /// <summary>The detent value (0..1) for a mode: EQ=0, DEEP=0.5, KILL=1.</summary>
    public static double ToValue(EqCutMode mode) => (double)(int)mode / (DetentCount - 1);

    private static EqCutMode ToMode(double value) => (EqCutMode)DetentIndex(value);

    private static double Snap(double value) => (double)DetentIndex(value) / (DetentCount - 1);

    private static int DetentIndex(double value)
        => (int)Math.Round(Math.Clamp(value, 0.0, 1.0) * (DetentCount - 1), MidpointRounding.AwayFromZero);
}
