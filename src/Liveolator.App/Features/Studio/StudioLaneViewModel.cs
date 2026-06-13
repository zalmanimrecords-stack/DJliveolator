using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Studio;
using ReactiveUI;

namespace Liveolator.App.Features.Studio;

/// <summary>
/// One deck lane on the STUDIO timeline: its slot, label, the clips placed on it, and its
/// per-lane automation (Ableton/Cubase-style — the envelope lives in the lane, not a separate
/// panel). A small per-lane selector chooses which control's curve the lane shows/edits.
/// </summary>
public sealed class StudioLaneViewModel : ViewModelBase
{
    private readonly Dictionary<AutomationTarget, AutomationLaneViewModel> _automation = new();
    private AutomationTarget _selectedAutomationTarget = AutomationTarget.DeckGain;

    public StudioLaneViewModel(int slot, string label)
    {
        Slot = slot;
        Label = label;
    }

    public int Slot { get; }

    /// <summary>"A"/"B" for the live decks, "C"/"D" for the hidden STUDIO decks.</summary>
    public string Label { get; }

    public ObservableCollection<StudioClipViewModel> Clips { get; } = new();

    /// <summary>The controls whose automation this lane can show (bound to the lane's target picker).</summary>
    public static IReadOnlyList<AutomationTarget> AutomationTargets { get; } = System.Enum.GetValues<AutomationTarget>();

    /// <summary>Which control's curve the lane currently shows/edits.</summary>
    public AutomationTarget SelectedAutomationTarget
    {
        get => _selectedAutomationTarget;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAutomationTarget, value);
            this.RaisePropertyChanged(nameof(CurrentAutomation));
        }
    }

    /// <summary>The editable curve for the selected target on this deck (created on first edit).</summary>
    public AutomationLaneViewModel CurrentAutomation => GetOrCreate(_selectedAutomationTarget);

    /// <summary>Every non-empty automation curve on this lane, as immutable Core lanes (for save/render).</summary>
    public IEnumerable<AutomationLane> NonEmptyAutomation()
        => _automation.Values.Where(l => l.Points.Count > 0).Select(l => l.ToLane());

    /// <summary>Load an automation curve onto this lane (from a saved project).</summary>
    public void SetAutomation(AutomationLane lane)
    {
        _automation[lane.Target] = new AutomationLaneViewModel(lane.Target, Slot, lane.Keyframes);
        if (lane.Target == _selectedAutomationTarget)
            this.RaisePropertyChanged(nameof(CurrentAutomation));
    }

    /// <summary>Drop all automation on this lane (New project).</summary>
    public void ClearAutomation()
    {
        _automation.Clear();
        this.RaisePropertyChanged(nameof(CurrentAutomation));
    }

    private AutomationLaneViewModel GetOrCreate(AutomationTarget target)
    {
        if (_automation.TryGetValue(target, out AutomationLaneViewModel? lane))
            return lane;
        lane = new AutomationLaneViewModel(target, Slot);
        _automation[target] = lane;
        return lane;
    }
}
