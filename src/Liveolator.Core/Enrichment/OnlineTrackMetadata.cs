namespace Liveolator.Core.Enrichment;

/// <summary>
/// Metadata returned by an online <see cref="IMetadataProvider"/> for a track (doc 16). All fields are
/// optional — a provider returns what it has. <see cref="Source"/> names the provider so the merged
/// result carries provenance. Pure data; the binding maps each provider's response into this shape.
/// </summary>
/// <param name="Bpm">Tempo in BPM, or null when the provider has none.</param>
/// <param name="Camelot">Camelot harmonic-mixing code (e.g. "8A"), or null.</param>
/// <param name="KeyName">Human-readable key (e.g. "A Minor"), or null.</param>
/// <param name="Genre">Genre/style, or null.</param>
/// <param name="Source">Provider name the data came from (e.g. "GetSongBPM", "MusicBrainz").</param>
public sealed record OnlineTrackMetadata(
    double? Bpm,
    string? Camelot,
    string? KeyName,
    string? Genre,
    string Source);
