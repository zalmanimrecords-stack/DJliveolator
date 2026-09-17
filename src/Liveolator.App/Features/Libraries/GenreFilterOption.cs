using ReactiveUI;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// One genre in the Libraries multi-select genre facet: the tag as the catalog spells it, plus whether
/// it is currently checked. Notifies through a callback rather than an observable so the options carry
/// no subscription to dispose — they live and die with the facet collection that rebuilds after a scan.
/// </summary>
public sealed class GenreFilterOption : ReactiveObject
{
    private readonly Action _onToggled;
    private bool _isSelected;

    /// <param name="isSelected">Initial state, assigned without notifying — rebuilding the facet after a
    /// scan restores the previous selection and must not read as the user re-picking it.</param>
    public GenreFilterOption(string name, bool isSelected, Action onToggled)
    {
        Name = name;
        _isSelected = isSelected;
        _onToggled = onToggled;
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
}
