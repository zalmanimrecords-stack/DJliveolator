using System.Globalization;
using System.Text;
using System.Text.Json;
using Liveolator.Core.Library.Music;

namespace Liveolator.Media;

/// <summary>Output formats for an exported playlist.</summary>
public enum PlaylistFormat
{
    /// <summary>Extended M3U (`.m3u8`) with #EXTINF duration/title lines — opens in most players.</summary>
    M3U8,

    /// <summary>JSON with per-track analysis (path, title, BPM, Camelot, duration) for tooling.</summary>
    Json
}

/// <summary>
/// Writes an ordered set of analyzed tracks to a playlist file. UTF-8 throughout; the caller
/// supplies the destination path and format.
/// </summary>
public sealed class PlaylistWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(
        IReadOnlyList<MusicTrack> tracks, string outputPath, PlaylistFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string content = format switch
        {
            PlaylistFormat.M3U8 => BuildM3U8(tracks),
            PlaylistFormat.Json => BuildJson(tracks),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown playlist format.")
        };

        await File.WriteAllTextAsync(outputPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildM3U8(IReadOnlyList<MusicTrack> tracks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        foreach (MusicTrack track in tracks)
        {
            int seconds = track.Duration is { } d ? (int)Math.Round(d.TotalSeconds) : -1;
            sb.Append("#EXTINF:").Append(seconds.ToString(CultureInfo.InvariantCulture)).Append(',').AppendLine(track.Title);
            sb.AppendLine(track.File.Path);
        }
        return sb.ToString();
    }

    private static string BuildJson(IReadOnlyList<MusicTrack> tracks)
    {
        var items = tracks.Select(t => new PlaylistItem(
            t.File.Path,
            t.Title,
            t.Bpm?.Bpm,
            t.Key?.Camelot,
            t.Key?.Name,
            t.Duration?.TotalSeconds)).ToList();
        return JsonSerializer.Serialize(items, JsonOptions);
    }

    private sealed record PlaylistItem(
        string Path, string Title, double? Bpm, string? Camelot, string? Key, double? DurationSeconds);
}
