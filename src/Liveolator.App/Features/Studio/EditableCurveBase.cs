using System.Collections.ObjectModel;
using Liveolator.App.Shell;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// Shared editing behaviour for a 0..1 keyframe curve: a time-ordered point collection with add /
/// remove / set-at-time (the freehand draw step). Subclasses add meaning to the value
/// (<see cref="AutomationLaneViewModel"/> = a deck-control 0..1, <see cref="TempoLaneViewModel"/> =
/// BPM mapped to 0..1), so one <see cref="Liveolator.App.Controls.AutomationCurveEditor"/> edits both.
/// </summary>
public abstract class EditableCurveBase : ViewModelBase, IEditableCurve
{
    public ObservableCollection<AutomationPointViewModel> Points { get; } = new();

    /// <summary>Add a keyframe, keeping the collection time-ordered, and return it.</summary>
    public AutomationPointViewModel AddPoint(double timeSeconds, double value)
    {
        var point = new AutomationPointViewModel(timeSeconds, value);
        int index = 0;
        while (index < Points.Count && Points[index].TimeSeconds <= point.TimeSeconds)
            index++;
        Points.Insert(index, point);
        return point;
    }

    public void RemovePoint(AutomationPointViewModel point) => Points.Remove(point);

    /// <summary>
    /// Set the value at <paramref name="timeSeconds"/>, replacing any keyframe within
    /// <paramref name="mergeToleranceSeconds"/> — the per-sample primitive of a freehand "draw" stroke.
    /// </summary>
    public AutomationPointViewModel SetPointAt(double timeSeconds, double value, double mergeToleranceSeconds)
    {
        double t = System.Math.Max(0, timeSeconds);
        for (int i = Points.Count - 1; i >= 0; i--)
            if (System.Math.Abs(Points[i].TimeSeconds - t) <= mergeToleranceSeconds)
                Points.RemoveAt(i);
        return AddPoint(t, value);
    }
}
