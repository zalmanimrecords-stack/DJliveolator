using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Studio;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// An editable automation curve for one (deck, target) pair: a time-ordered set of
/// <see cref="AutomationPointViewModel"/>s the curve editor draws and mutates. Keeps its points
/// sorted by time; <see cref="ToLane"/> projects to the immutable Core <see cref="AutomationLane"/>.
/// </summary>
public sealed class AutomationLaneViewModel : ViewModelBase
{
    public AutomationLaneViewModel(AutomationTarget target, int deckSlot, IEnumerable<AutomationKeyframe>? keyframes = null)
    {
        Target = target;
        DeckSlot = deckSlot;
        if (keyframes is not null)
            foreach (AutomationKeyframe k in keyframes.OrderBy(k => k.TimeSeconds))
                Points.Add(new AutomationPointViewModel(k.TimeSeconds, k.Value));
    }

    public AutomationTarget Target { get; }
    public int DeckSlot { get; }
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

    /// <summary>Project to the immutable Core lane (keyframes sorted by time).</summary>
    public AutomationLane ToLane() => new(
        Target,
        DeckSlot,
        Points.OrderBy(p => p.TimeSeconds).Select(p => p.ToKeyframe()).ToList());
}
