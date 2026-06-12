namespace Liveolator.Core.Studio;

/// <summary>
/// A named, ordered set planned ahead of the gig in the STUDIO tab: a sequence of
/// <see cref="StudioEntry"/>s, each carrying the transition that leads into it. The richer
/// successor to <see cref="Playlist.Playlist"/> — where a Playlist is just ordered paths, a
/// StudioSet also captures the planned transitions (length/curve/anchor) so the set can be played
/// live by the automix engine or rendered to an audio file. Pure data; the on-disk version lives
/// on the Media snapshot (see <c>JsonStudioSetStore</c>), mirroring the Playlist convention.
/// </summary>
public sealed record StudioSet(string Name, IReadOnlyList<StudioEntry> Entries)
{
    /// <summary>An empty set with the given name.</summary>
    public static StudioSet Empty(string name) => new(name, Array.Empty<StudioEntry>());

    /// <summary>Returns a copy with the given ordered entries (the name is unchanged).</summary>
    public StudioSet WithEntries(IEnumerable<StudioEntry> entries)
        => this with { Entries = entries?.ToList() ?? throw new ArgumentNullException(nameof(entries)) };

    /// <summary>The track paths in order — the projection consumed by the live queue and exporters.</summary>
    public IReadOnlyList<string> TrackPaths => Entries.Select(e => e.TrackPath).ToList();
}
