using System.Collections.Generic;
using System.Linq;
using Liveolator.Core.Studio;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// An editable automation curve for one (deck, target) pair: a time-ordered set of
/// <see cref="AutomationPointViewModel"/>s (0..1) the curve editor draws and mutates.
/// <see cref="ToLane"/> projects to the immutable Core <see cref="AutomationLane"/>.
/// </summary>
public sealed class AutomationLaneViewModel : EditableCurveBase
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

    /// <summary>Project to the immutable Core lane (keyframes sorted by time).</summary>
    public AutomationLane ToLane() => new(
        Target,
        DeckSlot,
        Points.OrderBy(p => p.TimeSeconds).Select(p => p.ToKeyframe()).ToList());
}
