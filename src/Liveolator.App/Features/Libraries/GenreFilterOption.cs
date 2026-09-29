using System.Linq;
using ReactiveUI;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// One genre in the Libraries multi-select genre facet: the tag as the catalog spells it, whether it is
/// currently checked, and (Phase 3) the "Group under…" curation control that lets the owner assign it a
/// parent genre. Notifies through callbacks rather than observables so the options carry no subscription
/// to dispose — they live and die with the facet collection that rebuilds after a scan.
/// </summary>
public sealed class GenreFilterOption : ReactiveObject
{
    /// <summary>The "Group under…" choice that clears a genre back to top-level.</summary>
    public const string TopLevelOption = "(top level)";

    private readonly Action _onToggled;
    private readonly Action<string?> _onGroupUnderChanged;
    private bool _isSelected;
    private string _groupUnderSelection;

    /// <param name="isSelected">Initial state, assigned without notifying — rebuilding the facet after a
    /// scan restores the previous selection and must not read as the user re-picking it.</param>
    /// <param name="parent">This genre's current parent, or null when it is top-level.</param>
    /// <param name="canGroupUnder">False when this genre already has children of its own — grouping it
    /// under another genre would push those children to a third level, so the control that could do that
    /// is hidden entirely rather than offered and rejected (the depth guard in <see cref="Core.Library.Music.GenreHierarchy"/>).</param>
    /// <param name="groupUnderCandidates">The other top-level genres this one could be grouped under
    /// (never includes this genre itself).</param>
    /// <param name="onGroupUnderChanged">Invoked with the new parent (null for "top level") when the
    /// grouping combo's selection changes.</param>
    public GenreFilterOption(
        string name,
        bool isSelected,
        Action onToggled,
        string? parent = null,
        bool canGroupUnder = true,
        IReadOnlyList<string>? groupUnderCandidates = null,
        Action<string?>? onGroupUnderChanged = null)
    {
        Name = name;
        _isSelected = isSelected;
        _onToggled = onToggled;
        Parent = parent;
        CanGroupUnder = canGroupUnder;
        GroupUnderOptions = new[] { TopLevelOption }.Concat(groupUnderCandidates ?? Array.Empty<string>()).ToList();
        _onGroupUnderChanged = onGroupUnderChanged ?? (_ => { });
        _groupUnderSelection = parent ?? TopLevelOption;
    }

    /// <summary>The genre tag exactly as the catalog holds it, which is what the user recognises.</summary>
    public string Name { get; }

    /// <summary>Whether this genre is part of the current filter. Checking any genre re-runs the query.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            this.RaiseAndSetIfChanged(ref _isSelected, value);
            _onToggled();
        }
    }

    /// <summary>This genre's parent at the time this option was built, or null when top-level.</summary>
    public string? Parent { get; }

    /// <summary>Whether the "Group under…" control should be shown for this row (false when this genre
    /// already has children — see the constructor note).</summary>
    public bool CanGroupUnder { get; }

    /// <summary>"(top level)" plus every other top-level genre — the only valid "Group under…" targets,
    /// so the combo can never offer a choice the depth guard would reject.</summary>
    public IReadOnlyList<string> GroupUnderOptions { get; }

    /// <summary>The grouping combo's bound selection: the current parent, or <see cref="TopLevelOption"/>.
    /// Changing it re-parents the genre (and persists the change) through the owning view-model.</summary>
    public string GroupUnderSelection
    {
        get => _groupUnderSelection;
        set
        {
            if (_groupUnderSelection == value)
                return;

            this.RaiseAndSetIfChanged(ref _groupUnderSelection, value);
            _onGroupUnderChanged(value == TopLevelOption ? null : value);
        }
    }
}
