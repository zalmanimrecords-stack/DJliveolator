namespace Liveolator.Core.Visuals.TrackPrograms;

/// <summary>One visual asset scheduled on the original music-track timeline.</summary>
public sealed record TrackVisualCue
{
    public TrackVisualCue(
        string id,
        VisualAssetReference asset,
        TimeSpan startAt,
        TimeSpan? endAt,
        TimeSpan? sourceIn,
        TimeSpan? sourceOut,
        VisualFitMode fit,
        VisualPlaybackMode playback,
        TransitionStyle transition,
        double opacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(asset);
        if (startAt < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startAt));
        if (endAt is { } end && end <= startAt)
            throw new ArgumentException("Cue end must be later than its start.", nameof(endAt));
        if (sourceIn is { } sourceStart && sourceStart < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sourceIn));
        if (sourceOut is { } sourceEnd && sourceEnd < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sourceOut));
        if (sourceOut is { } outTime && outTime <= (sourceIn ?? TimeSpan.Zero))
            throw new ArgumentException("Source out must be later than source in.", nameof(sourceOut));
        if (double.IsNaN(opacity) || opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "Opacity must be in 0..1.");

        Id = id;
        Asset = asset;
        StartAt = startAt;
        EndAt = endAt;
        SourceIn = sourceIn;
        SourceOut = sourceOut;
        Fit = fit;
        Playback = playback;
        Transition = transition;
        Opacity = opacity;
    }

    public string Id { get; init; }
    public VisualAssetReference Asset { get; init; }
    public TimeSpan StartAt { get; init; }
    public TimeSpan? EndAt { get; init; }
    public TimeSpan? SourceIn { get; init; }
    public TimeSpan? SourceOut { get; init; }
    public VisualFitMode Fit { get; init; }
    public VisualPlaybackMode Playback { get; init; }
    public TransitionStyle Transition { get; init; }
    public double Opacity { get; init; }
}
