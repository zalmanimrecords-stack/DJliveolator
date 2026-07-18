using Liveolator.App.Features.Shared;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Libraries;

/// <summary>Display wrapper over a <see cref="MusicTrack"/> for the library table and detail panel.</summary>
public sealed class TrackRowViewModel
{
    private const string None = "—";

    public TrackRowViewModel(MusicTrack track, TrackContextActions? contextActions = null, bool hasCues = false)
    {
        Track = track ?? throw new ArgumentNullException(nameof(track));
        HasCues = hasCues;
        Menu = contextActions is null
            ? null
            : new TrackMenuViewModel(
                track.File.Path,
                contextActions,
                track.Bpm?.Bpm ?? 0,
                track.Bpm?.FirstBeatSeconds ?? 0,
                track.Bpm?.KickOnsetsSeconds);
    }

    public MusicTrack Track { get; }

    // --- per-component analysis presence (drives the row status badges) ---

    /// <summary>True when a tempo was detected (analyzed BPM &gt; 0).</summary>
    public bool HasBpm => Track.Bpm is { } bpm && bpm.Bpm > 0;

    /// <summary>True when a musical key was detected.</summary>
    public bool HasKey => Track.Key is not null;

    /// <summary>True when the file's tag metadata carries a genre.</summary>
    public bool HasGenre => !string.IsNullOrWhiteSpace(Track.Metadata?.Genre);

    /// <summary>True when offline song-structure segmentation is present (doc 32).</summary>
    public bool HasStructure => Track.Structure is not null;

    /// <summary>True when the track has at least one stored hot cue (read in batch from the cue store).</summary>
    public bool HasCues { get; }

    // --- online BPM cross-check (doc 16) ---

    /// <summary>True when the detected BPM disagrees with the online value — the review flag.</summary>
    public bool IsBpmConflicted => Track.BpmProvenance == BpmProvenance.Conflicted;

    /// <summary>Theme token for the BPM badge: Red on a conflict, otherwise the normal presence colors.</summary>
    public string BpmBadgeToken => IsBpmConflicted ? "Red" : HasBpm ? "Accent" : "Faint";

    /// <summary>
    /// BPM badge tooltip. On a conflict it names BOTH values and the online source — the source name is
    /// the GetSongBPM attribution requirement, not decoration.
    /// </summary>
    public string BpmBadgeTip => Track.BpmProvenance switch
    {
        BpmProvenance.Conflicted =>
            $"BPM conflict — detected {Track.Bpm?.Bpm:0.0} · {Track.OnlineBpmSource} says {Track.OnlineBpm:0.0}. " +
            "Right-click to keep the detected value, or re-analyze.",
        BpmProvenance.CrossChecked => $"BPM cross-checked online ({Track.OnlineBpmSource})",
        BpmProvenance.OnlineFetched => $"BPM from {Track.OnlineBpmSource} (not verified against the file)",
        BpmProvenance.LocalConfirmed => "BPM confirmed by you",
        _ => HasBpm ? "Detected BPM" : "No BPM analyzed",
    };

    /// <summary>Detail-panel line for the online value; blank when the track was never matched online.</summary>
    public string OnlineBpmDetail => Track.OnlineBpm is { } online
        ? IsBpmConflicted
            ? $"⚠ {Track.OnlineBpmSource}: {online:0.0} BPM — differs from the detected value"
            : $"{Track.OnlineBpmSource}: {online:0.0} BPM"
        : string.Empty;

    /// <summary>Theme token for the detail line: Red on a conflict, quiet otherwise.</summary>
    public string OnlineBpmDetailToken => IsBpmConflicted ? "Red" : "Faint";

    /// <summary>Right-click menu (Add to Deck A/B, Add to playlist); null when context actions weren't supplied.</summary>
    public TrackMenuViewModel? Menu { get; }

    // --- table columns ---
    public string Title => Track.Title;
    public string Artist => Track.Artist ?? None;
    public string Bpm => Track.Bpm is { } bpm ? bpm.Bpm.ToString("0.0") : None;
    public string Key => Track.Key?.Camelot ?? None;
    public string Duration => Track.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : None;

    public MediaAnalysisStatus Status => Track.Status;

    // --- library-management fields (the prepare workflow) ---

    /// <summary>The user's 0–5 star rating (0 = unrated).</summary>
    public int Rating => Track.Rating;

    /// <summary>Rating as filled/empty stars for a compact display, e.g. "★★★☆☆"; blank when unrated.</summary>
    public string RatingStars =>
        Track.Rating <= 0 ? string.Empty : new string('★', Track.Rating) + new string('☆', 5 - Track.Rating);

    /// <summary>True once the track has been loaded to a deck at least once (drives a "played" marker).</summary>
    public bool IsPlayed => Track.PlayCount > 0;

    /// <summary>"Played N×" when the track has plays, otherwise blank.</summary>
    public string PlayCountText => Track.PlayCount > 0 ? $"Played {Track.PlayCount}×" : string.Empty;

    /// <summary>When the track was added to the library (local date), or blank if never stamped.</summary>
    public string DateAddedText =>
        Track.DateAdded is { } added ? $"Added {added.ToLocalTime():yyyy-MM-dd}" : string.Empty;

    /// <summary>When the track was last scanned/analyzed (local date + time), or blank if never stamped.</summary>
    public string LastScannedText =>
        Track.LastAnalyzedUtc is { } scanned ? $"Scanned {scanned.ToLocalTime():yyyy-MM-dd HH:mm}" : string.Empty;

    public string StatusText => Track.Status switch
    {
        MediaAnalysisStatus.Ok => "OK",
        MediaAnalysisStatus.PartiallyAnalyzed => "Partial",
        _ => "Failed",
    };

    // --- detail panel ---

    /// <summary>"Artist · bitrate · codec" — omits the parts that are unknown. The folder is deliberately
    /// left out: the full path already has its own row in the INFO section, so it isn't shown twice.</summary>
    public string SubLine
    {
        get
        {
            var parts = new[]
            {
                Track.Artist,
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
