using Liveolator.Core.Analysis;
using Liveolator.Core.Library.Music;

namespace Liveolator.Mcp.Contracts;

/// <summary>Auto-detected intro/outro markers (seconds from track start); null where unknown.</summary>
public sealed record CueInfo(
    double? IntroStartSeconds,
    double? IntroEndSeconds,
    double? OutroStartSeconds,
    double? OutroEndSeconds);

/// <summary>
/// Stable, agent-facing view of one analyzed track. Decoupled from the internal
/// <see cref="MusicTrack"/> record so the tool contract stays consistent even if Core changes
/// (global standard #23).
/// </summary>
public sealed record TrackInfo(
    string Path,
    string Title,
    double? Bpm,
    double? BpmConfidence,
    string? Key,
    string? Camelot,
    double? KeyConfidence,
    double? DurationSeconds,
    CueInfo Cues,
    string Status,
    string? Error)
{
    public static TrackInfo From(MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new TrackInfo(
            track.File.Path,
            track.Title,
            track.Bpm?.Bpm,
            track.Bpm?.Confidence,
            track.Key?.Name,
            track.Key?.Camelot,
            track.Key?.Confidence,
            track.Duration?.TotalSeconds,
            new CueInfo(
                track.Cues.IntroStart?.TotalSeconds,
                track.Cues.IntroEnd?.TotalSeconds,
                track.Cues.OutroStart?.TotalSeconds,
                track.Cues.OutroEnd?.TotalSeconds),
            track.Status.ToString(),
            track.Error);
    }

    /// <summary>Builds a view from a one-off analysis result (the ad-hoc analyze tool, no catalog entry).</summary>
    public static TrackInfo FromAnalysis(string path, TrackAnalysisResult result)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(result);
        return new TrackInfo(
            path,
            System.IO.Path.GetFileNameWithoutExtension(path),
            result.Bpm.Bpm,
            result.Bpm.Confidence,
            result.Key.Name,
            result.Key.Camelot,
            result.Key.Confidence,
            result.Duration.TotalSeconds,
            new CueInfo(
                result.Cues.IntroStart?.TotalSeconds,
                result.Cues.IntroEnd?.TotalSeconds,
                result.Cues.OutroStart?.TotalSeconds,
                result.Cues.OutroEnd?.TotalSeconds),
            TrackStatusPolicy.For(result).ToString(),
            Error: null);
    }
}
