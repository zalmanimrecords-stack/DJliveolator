using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Libraries;

/// <summary>Display wrapper over a <see cref="MusicTrack"/> for the library table.</summary>
public sealed class TrackRowViewModel
{
    public TrackRowViewModel(MusicTrack track)
    {
        Track = track ?? throw new ArgumentNullException(nameof(track));
    }

    public MusicTrack Track { get; }

    public string Title => Track.Title;

    public string Bpm => Track.Bpm is { } bpm ? bpm.Bpm.ToString("0.0") : "—";

    public string Key => Track.Key?.Camelot ?? "—";

    public string Duration => Track.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : "—";

    public MediaAnalysisStatus Status => Track.Status;

    public string StatusText => Track.Status switch
    {
        MediaAnalysisStatus.Ok => "OK",
        MediaAnalysisStatus.PartiallyAnalyzed => "Partial",
        _ => "Failed",
    };

    /// <summary>Case-insensitive match against title or Camelot key, for the search box.</summary>
    public bool Matches(string query)
        => Title.Contains(query, StringComparison.OrdinalIgnoreCase)
           || (Track.Key?.Camelot.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
}
