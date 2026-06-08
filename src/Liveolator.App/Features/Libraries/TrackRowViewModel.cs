using System.IO;
using Liveolator.App.Features.Shared;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Libraries;

/// <summary>Display wrapper over a <see cref="MusicTrack"/> for the library table and detail panel.</summary>
public sealed class TrackRowViewModel
{
    private const string None = "—";

    public TrackRowViewModel(MusicTrack track, TrackContextActions? contextActions = null)
    {
        Track = track ?? throw new ArgumentNullException(nameof(track));
        Menu = contextActions is null
            ? null
            : new TrackMenuViewModel(
                track.File.Path, contextActions, track.Bpm?.Bpm ?? 0, track.Bpm?.FirstBeatSeconds ?? 0);
    }

    public MusicTrack Track { get; }

    /// <summary>Right-click menu (Add to Deck A/B, Add to playlist); null when context actions weren't supplied.</summary>
    public TrackMenuViewModel? Menu { get; }

    // --- table columns ---
    public string Title => Track.Title;
    public string Artist => Track.Artist ?? None;
    public string Bpm => Track.Bpm is { } bpm ? bpm.Bpm.ToString("0.0") : None;
    public string Key => Track.Key?.Camelot ?? None;
    public string Duration => Track.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : None;

    public MediaAnalysisStatus Status => Track.Status;

    public string StatusText => Track.Status switch
    {
        MediaAnalysisStatus.Ok => "OK",
        MediaAnalysisStatus.PartiallyAnalyzed => "Partial",
        _ => "Failed",
    };

    // --- detail panel ---

    /// <summary>"Artist · folder · bitrate · format" — omits the parts that are unknown.</summary>
    public string SubLine
    {
        get
        {
            string? folder = Path.GetDirectoryName(Track.File.Path);
            var parts = new[]
            {
                Track.Artist,
                string.IsNullOrEmpty(folder) ? null : folder,
                Track.Metadata?.BitrateKbps is { } kbps ? $"{kbps}kbps" : null,
                Track.Metadata?.Codec,
            };
            string joined = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            return joined.Length == 0 ? StatusText : joined;
        }
    }

    public string Confidence => Track.Bpm is { } bpm ? $"{bpm.Confidence * 100:0}%" : None;
    public string KeyName => Track.Key?.Name ?? None;

    public string Album => Track.Metadata?.Album ?? None;
    public string Genre => Track.Metadata?.Genre ?? None;
    public string Year => Track.Metadata?.Year?.ToString() ?? None;
    public string TrackNo => Track.Metadata?.TrackNumber?.ToString() ?? None;
    public string Notes => Track.Metadata?.Comment ?? None;

    public string SampleRate =>
        Track.Metadata?.SampleRateHz is { } hz ? $"{hz / 1000.0:0.#} kHz" : None;

    public string Channels => Track.Metadata?.Channels switch
    {
        1 => "Mono",
        2 => "Stereo",
        { } n => $"{n} ch",
        _ => None,
    };

    public string Codec => Track.Metadata?.Codec ?? None;

    /// <summary>Case-insensitive match against title, artist, album, or Camelot key, for the search box.</summary>
    public bool Matches(string query)
        => Contains(Title, query)
           || Contains(Track.Artist, query)
           || Contains(Track.Metadata?.Album, query)
           || Contains(Track.Key?.Camelot, query);

    private static bool Contains(string? value, string query)
        => value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
