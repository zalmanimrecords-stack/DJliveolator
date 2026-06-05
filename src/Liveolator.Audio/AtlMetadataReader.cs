using ATL;
using Liveolator.Core.Library.Music;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Audio;

/// <summary>
/// Reads tag + stream metadata via ATL.NET (z440.atl.core, MIT) — pure managed, no native deps,
/// cross-platform. The concrete <see cref="ITrackMetadataReader"/> for the Core library seam.
/// A file with no readable tags or an unparseable file degrades to <c>null</c> (logged), never
/// an exception, so a single bad file never aborts a library scan (global standards #16/#26).
/// </summary>
public sealed class AtlMetadataReader : ITrackMetadataReader
{
    private readonly ILogger<AtlMetadataReader> _logger;

    static AtlMetadataReader()
        // Report a genuinely-untagged file as having no title (Core falls back to the filename),
        // instead of ATL's default of synthesizing the title from the file name.
        => Settings.UseFileNameWhenNoTitle = false;

    public AtlMetadataReader(ILogger<AtlMetadataReader>? logger = null)
        => _logger = logger ?? NullLogger<AtlMetadataReader>.Instance;

    public TrackMetadata? Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            var track = new Track(filePath);
            return new TrackMetadata(
                Title: Clean(track.Title),
                Artist: Clean(track.Artist),
                Album: Clean(track.Album),
                AlbumArtist: Clean(track.AlbumArtist),
                Genre: Clean(track.Genre),
                Year: Positive(track.Year),
                TrackNumber: Positive(track.TrackNumber),
                Comment: Clean(track.Comment),
                BitrateKbps: Positive(track.Bitrate),
                SampleRateHz: Positive((int)track.SampleRate),
                Channels: Positive(track.ChannelsArrangement?.NbChannels ?? 0),
                Codec: Clean(track.AudioFormat?.ShortName));
        }
        catch (Exception ex)
        {
            // ATL surfaces unsupported/corrupt files as exceptions; treat as "no metadata".
            _logger.LogWarning(ex, "Could not read metadata from '{FilePath}'", filePath);
            return null;
        }
    }

    // ATL returns "" (not null) for absent string tags; normalize so consumers can null-check.
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ATL returns 0 (not null) for unknown numeric fields; normalize so the UI shows "—" not "0".
    private static int? Positive(int value) => value > 0 ? value : null;
    private static int? Positive(int? value) => value is > 0 ? value : null;
}
