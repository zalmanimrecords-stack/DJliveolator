namespace Liveolator.Core.Library.Music;

/// <summary>
/// An optional, user-curated parent/child grouping layered on top of the flat genre tag
/// (<see cref="GenreTag"/> deliberately stays a flat string operation — this is the "musical judgement"
/// escape hatch its docs point to). A genre with no assigned parent is top-level, exactly as every genre
/// behaves today: the mapping is additive, so an empty <see cref="GenreHierarchy"/> changes nothing
/// (zero-cost default).
/// </summary>
/// <remarks>
/// Immutable, like <see cref="Analysis.Cues.TrackCueSet"/>: every edit returns a new instance, so a
/// rejected <see cref="SetParent"/> call (see below) can never leave the caller holding a half-changed
/// tree — the original reference is simply untouched.
/// </remarks>
public sealed class GenreHierarchy
{
    private readonly Dictionary<string, string> _parentByGenre;

    public GenreHierarchy() : this(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
    {
    }

    private GenreHierarchy(Dictionary<string, string> parentByGenre) => _parentByGenre = parentByGenre;

    /// <summary>Rebuilds a hierarchy from persisted (genre, parent) pairs (genre names, not GenreTag tokens).</summary>
    public static GenreHierarchy FromMappings(IReadOnlyDictionary<string, string> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string genre, string parent) in mappings)
        {
            if (!string.IsNullOrWhiteSpace(genre) && !string.IsNullOrWhiteSpace(parent))
                map[genre] = parent;
        }

        return new GenreHierarchy(map);
    }

    /// <summary>The raw (genre, parent) pairs, for a store to persist.</summary>
    public IReadOnlyDictionary<string, string> Mappings => _parentByGenre;

    /// <summary>The parent of <paramref name="genre"/>, or null when it is top-level (unmapped).</summary>
    public string? ParentOf(string genre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(genre);
        return _parentByGenre.TryGetValue(genre, out string? parent) ? parent : null;
    }

    /// <summary>The genres whose parent is <paramref name="parentGenre"/> (empty when it has none).</summary>
    public IReadOnlyList<string> ChildrenOf(string parentGenre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentGenre);
        return _parentByGenre
            .Where(kv => string.Equals(kv.Value, parentGenre, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>The genres in <paramref name="allRawGenres"/> that have no assigned parent.</summary>
    public IReadOnlyList<string> TopLevelGenres(IEnumerable<string> allRawGenres)
    {
        ArgumentNullException.ThrowIfNull(allRawGenres);
        return allRawGenres.Where(g => ParentOf(g) is null).ToList();
    }

    /// <summary>
    /// Returns a new hierarchy with <paramref name="genre"/>'s parent set to <paramref name="parent"/>,
    /// or cleared back to top-level when <paramref name="parent"/> is null. Rejects (throws
    /// <see cref="ArgumentException"/>, unchanged) a parent that would create a cycle — direct
    /// (<paramref name="genre"/> as its own parent) or transitive (parent already descends from genre).
    /// </summary>
    public GenreHierarchy SetParent(string genre, string? parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(genre);

        if (parent is null)
        {
            if (!_parentByGenre.ContainsKey(genre))
                return this;

            var cleared = new Dictionary<string, string>(_parentByGenre, StringComparer.OrdinalIgnoreCase);
            cleared.Remove(genre);
            return new GenreHierarchy(cleared);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(parent);

        if (WouldCreateCycle(genre, parent))
        {
            throw new ArgumentException(
                $"Setting '{parent}' as the parent of '{genre}' would create a cycle.", nameof(parent));
        }

        var map = new Dictionary<string, string>(_parentByGenre, StringComparer.OrdinalIgnoreCase) { [genre] = parent };
        return new GenreHierarchy(map);
    }

    // Walks parent -> its parent -> ... looking for genre. Reads only the CURRENT tree (this), so it
    // always runs before any mutation is built, guaranteeing a rejected call never touches state.
    private bool WouldCreateCycle(string genre, string parent)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? current = parent;
        while (current is not null)
        {
            if (string.Equals(current, genre, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!seen.Add(current)) // defensive: stop on a pre-existing cycle instead of looping forever
                return true;
            current = ParentOf(current);
        }

        return false;
    }
}
