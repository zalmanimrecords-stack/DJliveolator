namespace Liveolator.Mcp.Contracts;

/// <summary>One step of a generated set: the track and how it relates to the previous one.</summary>
public sealed record PlaylistStep(TrackInfo Track, string? Relationship, double? BpmDelta);

/// <summary>A generated harmonic set: ordered steps plus summary totals.</summary>
public sealed record PlaylistResult(
    IReadOnlyList<PlaylistStep> Steps,
    int Count,
    double TotalDurationSeconds);

/// <summary>One harmonically-compatible match for a seed track.</summary>
public sealed record HarmonicMatch(TrackInfo Track, string Relationship);

/// <summary>Result of writing a playlist to disk.</summary>
public sealed record PlaylistExportResult(string OutputPath, int TrackCount, string Format);
