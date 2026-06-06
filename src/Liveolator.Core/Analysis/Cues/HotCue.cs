namespace Liveolator.Core.Analysis.Cues;

/// <summary>
/// A single stored hot cue on a track (doc 11 — "8 hot cues per deck"): an indexed jump point
/// at an exact sample offset, with an optional performer label and display color. Positions are
/// kept in <em>samples</em> (not seconds) so recall is sample-accurate and independent of any
/// display rounding; the owning <see cref="TrackCueSet.SampleRate"/> converts to time when needed.
/// </summary>
/// <param name="Index">Zero-based hot-cue slot (0..N-1, where N is the deck's hot-cue count).</param>
/// <param name="PositionSamples">Cue position as a non-negative sample offset from track start.</param>
/// <param name="Label">Optional performer label (e.g. "Drop", "Verse"); null when unlabelled.</param>
/// <param name="Color">Optional 0xRRGGBB display color for pad/UI; null when unset.</param>
public readonly record struct HotCue(
    int Index,
    long PositionSamples,
    string? Label = null,
    int? Color = null)
{
    /// <summary>Cue position in seconds, given the track's sample rate (samples per second).</summary>
    public double PositionSeconds(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        return (double)PositionSamples / sampleRate;
    }
}
