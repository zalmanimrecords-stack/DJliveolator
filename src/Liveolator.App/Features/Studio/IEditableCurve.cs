using System.Collections.ObjectModel;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// The minimal contract the <see cref="Liveolator.App.Controls.AutomationCurveEditor"/> needs to draw
/// and edit a curve of 0..1 points over time. Implemented by both <see cref="AutomationLaneViewModel"/>
/// (per-deck automation) and <see cref="TempoLaneViewModel"/> (the project tempo curve, which maps its
/// 0..1 points to a BPM range), so one editor control serves both.
/// </summary>
public interface IEditableCurve
{
    ObservableCollection<AutomationPointViewModel> Points { get; }

    /// <summary>Add a keyframe (time-ordered) and return it.</summary>
    AutomationPointViewModel AddPoint(double timeSeconds, double value);

    /// <summary>Remove a keyframe.</summary>
    void RemovePoint(AutomationPointViewModel point);

    /// <summary>Set the value at a time, replacing any keyframe within the tolerance (freehand draw step).</summary>
    AutomationPointViewModel SetPointAt(double timeSeconds, double value, double mergeToleranceSeconds);
}
