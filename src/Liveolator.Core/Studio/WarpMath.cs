namespace Liveolator.Core.Studio;

/// <summary>
/// Pure warp geometry: how fast a clip's source is read per timeline second so it sounds at the
/// project tempo (keylock preserves pitch). The factor is instantaneous — under a moving tempo curve
/// the source position is the integral of the factor, which the render/transport accumulate per block/
/// tick; this type only answers "what is the factor at time t" and "how wide is a warped clip".
/// </summary>
public static class WarpMath
{
    /// <summary>
    /// Read-rate for <paramref name="clip"/> at <paramref name="timelineSeconds"/>:
    /// <c>tempoAt(t) / clip.SourceBpm</c> when the clip can warp, else <c>1.0</c> (native speed).
    /// &gt; 1 plays faster (warp up), &lt; 1 slower.
    /// </summary>
    public static double WarpFactorAt(StudioClip clip, TempoCurve tempo, double defaultBpm, double timelineSeconds)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(tempo);
        if (!clip.CanWarp)
            return 1.0;
        double bpm = tempo.TempoAt(timelineSeconds, defaultBpm);
        return bpm > 0.0 ? bpm / clip.SourceBpm : 1.0;
    }

    /// <summary>
    /// A source span warped at <paramref name="factor"/> occupies this many timeline seconds
    /// (<c>sourceSeconds / factor</c>): warping up shortens it, down lengthens it.
    /// </summary>
    public static double WarpedTimelineSeconds(double sourceSeconds, double factor)
        => factor > 0.0 ? sourceSeconds / factor : sourceSeconds;

    /// <summary>
    /// The clip's on-timeline width. 0 when the source length is unknown.
    /// <para>Under a flat tempo the factor sampled at the clip's start is the factor for its whole life.
    /// Under a moving one it is the tempo the clip has already left, so the width comes from the integral
    /// instead — measured: sampling the start cost a record 4.2 s of its own length, which ate a third of
    /// the blend after it and had the export gate report a 14 s mix as a 9.8 s cut.</para>
    /// </summary>
    public static double WarpedTimelineWidth(StudioClip clip, TempoCurve tempo, double defaultBpm)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(tempo);
        if (clip.SourceDuration is not { } duration)
            return 0.0;

        if (tempo.Keyframes.Count > 0 && clip.CanWarp)
        {
            return TempoIntegral.TimelineSecondsForSource(
                tempo, defaultBpm, clip.SourceBpm, clip.TimelineStartSeconds, duration.TotalSeconds)
                - clip.TimelineStartSeconds;
        }

        double factor = WarpFactorAt(clip, tempo, defaultBpm, clip.TimelineStartSeconds);
        return WarpedTimelineSeconds(duration.TotalSeconds, factor);
    }
}
