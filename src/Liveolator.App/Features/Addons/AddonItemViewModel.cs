using Liveolator.App.Shell;

namespace Liveolator.App.Features.Addons;

/// <summary>
/// One row in the Add-ons tab: a built-in visual add-on (e.g. the VU meter) or an installed extension
/// package. Presentation only — identity (<see cref="Id"/>) lets the parent decide which settings panel,
/// if any, to show when the row is selected.
/// </summary>
public sealed class AddonItemViewModel : ViewModelBase
{
    public AddonItemViewModel(
        string id,
        string title,
        string description,
        bool hasSettings,
        bool isBuiltIn,
        string state)
    {
        Id = id;
        Title = title;
        Description = description;
        HasSettings = hasSettings;
        IsBuiltIn = isBuiltIn;
        State = state;
    }

    /// <summary>Stable identity — an effect id for built-ins, a package id for installed extensions.</summary>
    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>True when selecting this row reveals a configuration panel (only the VU meter today).</summary>
    public bool HasSettings { get; }

    public bool IsBuiltIn { get; }

    /// <summary>Short status chip: "Built-in", or "Enabled"/"Disabled" for an installed package.</summary>
    public string State { get; }
}
