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
/// <param name="IsAuto">
/// True when this cue was placed by automatic analysis and not yet confirmed by the DJ (a "suggested"
/// cue, owner decision 2026-06-19). A manual cue — one the DJ set, moved, or committed by pressing it —
/// is <c>false</c> and is preserved verbatim across re-analysis (see auto-cue merge). Added after the
/// original shape with a default of <c>false</c> so existing serialized cues load as manual
/// (positional-record back-compat).
/// </param>
public readonly record struct HotCue(
    int Index,
    long PositionSamples,
    string? Label = null,
    int? Color = null,
    bool IsAuto = false)
{
    /// <summary>Cue position in seconds, given the track's sample rate (samples per second).</summary>
    public double PositionSeconds(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        return (double)PositionSamples / sampleRate;
    }
}
