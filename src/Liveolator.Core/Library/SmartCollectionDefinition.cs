using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Library.SmartCollections;

public sealed record SmartCollectionDefinition(
    string Name,
    TrackFilter Filter,
    TrackSortKey SortKey = TrackSortKey.Title,
    bool Descending = false)
{
    public IReadOnlyList<MusicTrack> Evaluate(IEnumerable<MusicTrack> tracks, int limit = TrackQuery.MaxResults)
        => TrackQuery.Query(tracks, Filter, SortKey, Descending, limit);
}

