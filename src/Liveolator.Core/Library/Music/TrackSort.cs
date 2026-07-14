using Liveolator.Core.Analysis.Key;

namespace Liveolator.Core.Library.Music;

/// <summary>
/// Pure, reusable ordering of a catalogued track set by a single <see cref="TrackSortKey"/> and a
/// direction. Tracks missing the sort value (no BPM/key/duration) always sort last, whichever the
/// direction, so a sort never buries the analyzed tracks behind blanks. Title is the stable
/// tie-break. IO-free, so it unit-tests over an in-memory set and is shared by the Libraries UI.
/// </summary>
public static class TrackSort
{
    /// <summary>
    /// Returns <paramref name="tracks"/> ordered by <paramref name="key"/>. When
    /// <paramref name="descending"/> the present values are reversed, but tracks without a value
    /// still sort last. Title (case-insensitive) breaks ties deterministically.
    /// </summary>
    public static IReadOnlyList<MusicTrack> Apply(
        IEnumerable<MusicTrack> tracks, TrackSortKey key, bool descending)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        if (key == TrackSortKey.Title)
        {
            // Title is always present (derived from the file name), so no "missing" partition.
            IOrderedEnumerable<MusicTrack> byTitle = descending
                ? tracks.OrderByDescending(t => t.Title, StringComparer.OrdinalIgnoreCase)
                : tracks.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase);
            return byTitle.ToList();
        }

        // Present-before-missing first, so blanks never lead in either direction.
        IOrderedEnumerable<MusicTrack> ordered = tracks.OrderByDescending(HasValue);

        ordered = key switch
        {
            TrackSortKey.Bpm => ThenByValue(ordered, t => t.Bpm?.Bpm, descending),
            TrackSortKey.Key => ThenByValue(ordered, t => (double)Camelot.SortIndex(t.Key?.Camelot), descending),
            TrackSortKey.Duration => ThenByValue(ordered, t => t.Duration?.TotalSeconds, descending),
            TrackSortKey.Rating => ThenByValue(ordered, t => t.Rating > 0 ? t.Rating : (double?)null, descending),
            TrackSortKey.DateAdded => ThenByValue(ordered, t => t.DateAdded?.Ticks, descending),
            TrackSortKey.PlayCount => ThenByValue(ordered, t => (double)t.PlayCount, descending),
            _ => ordered,
        };

        return ordered
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool HasValue(MusicTrack t) => key switch
        {
            TrackSortKey.Bpm => t.Bpm is not null,
            TrackSortKey.Key => Camelot.SortIndex(t.Key?.Camelot) != int.MaxValue,
            TrackSortKey.Duration => t.Duration is not null,
            TrackSortKey.Rating => t.Rating > 0,
            TrackSortKey.DateAdded => t.DateAdded is not null,
            _ => true,
        };
    }

    // A nullable value sorts with nulls last (they are already partitioned by HasValue, so the null
    // ordering here is only a safety net) and applies the requested direction to the present values.
    private static IOrderedEnumerable<MusicTrack> ThenByValue(
        IOrderedEnumerable<MusicTrack> ordered, Func<MusicTrack, double?> selector, bool descending)
        => descending
            ? ordered.ThenByDescending(t => selector(t) ?? double.MinValue)
            : ordered.ThenBy(t => selector(t) ?? double.MaxValue);
}
