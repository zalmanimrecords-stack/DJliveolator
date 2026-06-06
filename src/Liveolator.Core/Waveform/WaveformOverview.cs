namespace Liveolator.Core.Waveform;

/// <summary>
/// A track's display waveform reduced to a fixed list of 0..1 peak magnitudes (max absolute amplitude
/// per bucket), independent of pixel width — the UI scales the buckets to the strip it draws. Computed
/// once per loaded track from decoded audio (doc 11 deck waveform); pure data so the reduction is
/// unit-tested with no native decode.
/// </summary>
/// <param name="Peaks">One 0..1 magnitude per bucket, left-to-right across the track.</param>
/// <param name="DurationSeconds">Decoded track length in seconds; 0 when unknown. Lets the UI place a
/// beat-grid overlay (beat interval ÷ duration = a 0..1 fraction) without a second decode.</param>
public sealed record WaveformOverview(IReadOnlyList<float> Peaks, double DurationSeconds = 0)
{
    /// <summary>Number of buckets.</summary>
    public int Count => Peaks.Count;

    /// <summary>True when there is no waveform data (e.g. an undecodable or empty track).</summary>
    public bool IsEmpty => Peaks.Count == 0;

    /// <summary>The "no waveform" value, used as the placeholder/degraded result.</summary>
    public static WaveformOverview Empty { get; } = new(Array.Empty<float>());
}
