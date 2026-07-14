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

    /// <summary>
    /// Invoked once at the start of each structural curve edit (add / remove / freehand-draw step),
    /// BEFORE the mutation is applied — the timeline VM wires this to its undo-snapshot push so
    /// automation/tempo edits are undoable. Null when no listener is attached (e.g. isolated control
    /// tests). The VM de-duplicates identical consecutive snapshots, so a no-op edit costs nothing.
    /// </summary>
    public Action? BeforeMutation { get; set; }

    /// <summary>Add a keyframe, keeping the collection time-ordered, and return it.</summary>
    public AutomationPointViewModel AddPoint(double timeSeconds, double value)
    {
        BeforeMutation?.Invoke();
        return Insert(timeSeconds, value);
    }

    public void RemovePoint(AutomationPointViewModel point)
    {
        BeforeMutation?.Invoke();
        Points.Remove(point);
    }

    /// <summary>
    /// Set the value at <paramref name="timeSeconds"/>, replacing any keyframe within
    /// <paramref name="mergeToleranceSeconds"/> — the per-sample primitive of a freehand "draw" stroke.
    /// </summary>
    public AutomationPointViewModel SetPointAt(double timeSeconds, double value, double mergeToleranceSeconds)
    {
        BeforeMutation?.Invoke();
        double t = System.Math.Max(0, timeSeconds);
        for (int i = Points.Count - 1; i >= 0; i--)
            if (System.Math.Abs(Points[i].TimeSeconds - t) <= mergeToleranceSeconds)
                Points.RemoveAt(i);
        return Insert(t, value);
    }

    // The time-ordered insert, with no BeforeMutation push — the public entry points fire it exactly
    // once so a freehand "draw" step (remove + insert) records a single undo snapshot, not two.
    private AutomationPointViewModel Insert(double timeSeconds, double value)
    {
        var point = new AutomationPointViewModel(timeSeconds, value);
        int index = 0;
        while (index < Points.Count && Points[index].TimeSeconds <= point.TimeSeconds)
            index++;
        Points.Insert(index, point);
        return point;
    }
}
