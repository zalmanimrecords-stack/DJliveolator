namespace Liveolator.Core.Library.Music;

/// <summary>
/// Tag + stream metadata read from an audio file (ID3 / Vorbis / MP4 / etc.), independent of
/// the offline BPM/key analysis. Every field is optional because tags are frequently missing
/// or partial; consumers fall back gracefully (e.g. title → filename) when a field is null.
/// </summary>
public sealed record TrackMetadata(
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    string? Genre,
    int? Year,
    int? TrackNumber,
    string? Comment,
    int? BitrateKbps,
    int? SampleRateHz,
    int? Channels,
    string? Codec)
{
    /// <summary>An all-null metadata record (no tags available).</summary>
    public static TrackMetadata Empty { get; } =
        new(null, null, null, null, null, null, null, null, null, null, null, null);
}
