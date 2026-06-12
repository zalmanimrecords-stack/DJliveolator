using System.Collections.ObjectModel;
using Liveolator.App.Shell;

namespace Liveolator.App.Features.Studio;

/// <summary>One deck lane on the STUDIO timeline: its slot, display label, and the clips placed on it.</summary>
public sealed class StudioLaneViewModel : ViewModelBase
{
    public StudioLaneViewModel(int slot, string label)
    {
        Slot = slot;
        Label = label;
    }

    public int Slot { get; }

    /// <summary>"A"/"B" for the live decks, "C"/"D" for the hidden STUDIO decks.</summary>
    public string Label { get; }

    public ObservableCollection<StudioClipViewModel> Clips { get; } = new();
}
