namespace Liveolator.Core.Audio.Sync;

/// <summary>
/// The pure beat-length → time-region calculation behind deck loops (doc 11): a DJ sets a loop in
/// musical beats (1, 2, 4, 8 …), and at the deck's tempo that maps to a concrete time span. Given a
/// loop start (seconds) and a beat length, this returns the loop's end time using the deck's base BPM,
/// so the engine can hand BASS a sample-accurate region.
/// </summary>
/// <remarks>
/// Pure and hardware-free so it unit-tests under xUnit; the engine converts the returned seconds to
/// sample positions for BASS. The tempo used is the deck's <c>base</c> BPM (the analyzed natural tempo
/// threaded in via <c>SetDeckBaseBpm</c>), so a 4-beat loop is musically four beats regardless of the
/// pitch fader — the loop region scales with playback rate exactly as the audio does.
/// </remarks>
public static class BeatLoopCalculator
{
    /// <summary>The shortest loop allowed, in beats — a sane lower bound for the loop encoder.</summary>
    public const double MinBeats = 1.0 / 32.0;

    /// <summary>
    /// The loop length in seconds for <paramref name="beats"/> at <paramref name="bpm"/>.
    /// </summary>
    /// <param name="beats">Loop length in beats (must be positive).</param>
    /// <param name="bpm">The deck's base tempo (BPM, must be positive).</param>
    public static double LengthSeconds(double beats, double bpm)
    {
        if (beats < MinBeats)
            throw new ArgumentOutOfRangeException(nameof(beats), beats, $"Loop length must be at least {MinBeats} beats.");
        if (bpm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "Loop requires a known (positive) deck BPM.");

        return beats * (60.0 / bpm);
    }

    /// <summary>
    /// The time region [start, end] (seconds) for a loop of <paramref name="beats"/> beats starting at
    /// <paramref name="startSeconds"/>, at the deck's <paramref name="bpm"/>.
    /// </summary>
    /// <param name="startSeconds">Loop in-point from the track start (seconds, must be non-negative).</param>
    /// <param name="beats">Loop length in beats (must be positive).</param>
    /// <param name="bpm">The deck's base tempo (BPM, must be positive).</param>
    public static LoopRegion Region(double startSeconds, double beats, double bpm)
    {
        if (startSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(startSeconds), startSeconds, "Loop start must be non-negative.");

        double length = LengthSeconds(beats, bpm);
        return new LoopRegion(startSeconds, startSeconds + length);
    }

    /// <summary>
    /// Snaps a loop in-point to the nearest beat boundary on the deck's grid (the first-beat anchor +
    /// tempo), so a loop set while Quantize is armed starts on the grid instead of wherever the playhead
    /// happened to sit (doc 27 B3). Returns a non-negative time; falls back to <paramref name="startSeconds"/>
    /// unchanged when the tempo is non-positive (no grid to snap to).
    /// </summary>
    /// <param name="startSeconds">The raw loop in-point (the current playhead), seconds from track start.</param>
    /// <param name="firstBeatSeconds">The track's first-beat (downbeat) anchor from analysis (seconds).</param>
    /// <param name="bpm">The deck's base tempo (BPM).</param>
    public static double SnapToBeat(double startSeconds, double firstBeatSeconds, double bpm)
    {
        if (bpm <= 0.0)
            return startSeconds;

        double beatSeconds = 60.0 / bpm;
        double beats = (startSeconds - firstBeatSeconds) / beatSeconds;
        double snapped = firstBeatSeconds + (Math.Round(beats) * beatSeconds);

        // Rounding can land just before the track start (a large anchor with an in-point near 0); step
        // forward to the first on-grid point that is non-negative so Region's contract still holds.
        if (snapped < 0.0)
            snapped += Math.Ceiling(-snapped / beatSeconds) * beatSeconds;
        return snapped;
    }
}

/// <summary>A loop's time region in seconds from the track start: a half-open [Start, End) span.</summary>
/// <param name="StartSeconds">Loop in-point (seconds).</param>
/// <param name="EndSeconds">Loop out-point (seconds).</param>
public readonly record struct LoopRegion(double StartSeconds, double EndSeconds)
{
    /// <summary>The loop length (seconds).</summary>
    public double LengthSeconds => EndSeconds - StartSeconds;
}
