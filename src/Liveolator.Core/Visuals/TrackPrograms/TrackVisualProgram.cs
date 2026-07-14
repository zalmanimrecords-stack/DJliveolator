namespace Liveolator.Core.Visuals.TrackPrograms;

/// <summary>
/// Authored visual timeline linked to one music track. Cues are normalized into start-time order and
/// validated as a single non-overlapping base-media lane.
/// </summary>
public sealed record TrackVisualProgram
{
    public TrackVisualProgram(
        string id,
        TrackReference track,
        IReadOnlyList<TrackVisualCue> cues,
        TrackVisualFallback fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(cues);

        if (cues.Any(cue => cue is null))
            throw new ArgumentException("Visual program cues cannot contain null entries.", nameof(cues));
        TrackVisualCue[] ordered = cues.OrderBy(cue => cue.StartAt).ToArray();
        if (ordered.Select(cue => cue.Id).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Visual program cue ids must be unique.", nameof(cues));

        for (int i = 0; i < ordered.Length - 1; i++)
        {
            if (ordered[i].EndAt is { } end && end > ordered[i + 1].StartAt)
                throw new ArgumentException("Visual program cues cannot overlap.", nameof(cues));
        }

        Id = id;
        Track = track;
        Cues = ordered;
        Fallback = fallback;
    }

    public string Id { get; init; }
    public TrackReference Track { get; init; }
    public IReadOnlyList<TrackVisualCue> Cues { get; init; }
    public TrackVisualFallback Fallback { get; init; }
}
