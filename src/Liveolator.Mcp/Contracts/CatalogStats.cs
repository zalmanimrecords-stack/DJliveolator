namespace Liveolator.Mcp.Contracts;

/// <summary>Number of tracks in a given Camelot key.</summary>
public sealed record KeyCount(string Camelot, int Count);

/// <summary>Number of tracks in a 10-BPM bucket, labelled by its lower bound (e.g. "120-129").</summary>
public sealed record BpmBucket(string Range, int Count);

/// <summary>Aggregate view of the catalog for exploration: status mix, key spread, tempo spread.</summary>
public sealed record CatalogStats(
    int Total,
    int Ok,
    int PartiallyAnalyzed,
    int Failed,
    double? AverageBpm,
    IReadOnlyList<KeyCount> KeyDistribution,
    IReadOnlyList<BpmBucket> BpmHistogram);
