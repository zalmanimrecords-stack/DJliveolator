namespace Liveolator.Core.Settings;

/// <summary>
/// Persisted visual/UI preferences (doc 12 Settings tab). Currently the deck waveform zoom: how many
/// seconds of audio the zoomed-in (playing) waveform shows around the playhead. A <em>smaller</em>
/// window means a more magnified ("zoomed-in") waveform; a larger window shows more of the track.
/// Pure data — persisted via <c>ISettingsStore</c>, clamped by <see cref="Normalized"/> so a stale or
/// hand-edited config can never push an unusable value into the deck UI.
/// </summary>
/// <param name="WaveformZoomSeconds">Seconds of audio shown in the zoomed deck waveform (lower = more zoomed in).</param>
/// <param name="NudgeSeconds">Seconds the deck track-nudge buttons move the playhead per press.</param>
// The parameter defaults are literals (a primary-ctor parameter default cannot reference a body-declared
// const); they MUST equal DefaultZoomSeconds / DefaultNudgeSeconds below.
public sealed record VisualsSettings(double WaveformZoomSeconds = 7.0, double NudgeSeconds = 0.1)
{
    /// <summary>Tightest zoom window offered (seconds) — the most magnified.</summary>
    public const double MinZoomSeconds = 2.0;

    /// <summary>Widest zoom window offered (seconds) — the least magnified.</summary>
    public const double MaxZoomSeconds = 30.0;

    /// <summary>Default zoom window (seconds): a few bars around the playhead so kicks read large.</summary>
    public const double DefaultZoomSeconds = 7.0;

    /// <summary>Smallest track-nudge step (seconds) — the finest cueing move.</summary>
    public const double MinNudgeSeconds = 0.02;

    /// <summary>Largest track-nudge step (seconds).</summary>
    public const double MaxNudgeSeconds = 2.0;

    /// <summary>Default track-nudge step (seconds): a fine 0.1 s cueing move per press.</summary>
    public const double DefaultNudgeSeconds = 0.1;

    /// <summary>The default visual preferences.</summary>
    public static VisualsSettings Default { get; } = new();

    /// <summary>Returns a copy with the zoom window + nudge step clamped into their supported ranges.</summary>
    public VisualsSettings Normalized()
        => this with
        {
            WaveformZoomSeconds = double.IsNaN(WaveformZoomSeconds)
                ? DefaultZoomSeconds
                : Math.Clamp(WaveformZoomSeconds, MinZoomSeconds, MaxZoomSeconds),
            NudgeSeconds = double.IsNaN(NudgeSeconds)
                ? DefaultNudgeSeconds
                : Math.Clamp(NudgeSeconds, MinNudgeSeconds, MaxNudgeSeconds),
        };
}
