using System;
using System.Collections.Generic;

namespace Liveolator.App.Features.Live.Modules;

/// <summary>
/// Derives the deck waveform's beat-grid overlay: the 0..1 track-position fractions at which beat lines
/// fall, from the track's tempo and duration. Pure presentation math (no engine call, no native) so it
/// unit-tests directly. Returns an empty grid — never throws — when the inputs are insufficient
/// (unknown BPM, zero/negative duration), so the strip simply draws no grid (global standards #16/#26).
/// </summary>
public static class BeatGridCalculator
{
    private const double SecondsPerMinute = 60.0;

    /// <summary>Guard against a pathological tempo producing a runaway number of lines on a long track.</summary>
    private const int MaxBeatLines = 4_096;

    /// <summary>Beats per bar (4/4) — the strip emphasises every fourth grid line (0, 4, 8 …) as a bar.</summary>
    public const int BeatsPerBar = 4;

    /// <summary>
    /// Beat-line positions as 0..1 fractions of the track, evenly spaced at the tempo's beat interval and
    /// anchored on the detected first beat (downbeat), so the grid lines fall on the track's kicks rather
    /// than on the raw track start. Index 0 is the first beat — the strip treats every fourth line as a
    /// bar downbeat (<see cref="BeatsPerBar"/>).
    /// </summary>
    /// <param name="bpm">Track tempo in beats per minute; 0 or negative means "unknown" → empty grid.</param>
    /// <param name="durationSeconds">Track length in seconds; 0 or negative → empty grid.</param>
    /// <param name="firstBeatSeconds">Analyzed first-beat offset in seconds (the downbeat anchor); 0 or an
    /// out-of-range value anchors at the track start (the pre-anchor behaviour).</param>
    public static IReadOnlyList<double> BeatFractions(
        double bpm, double durationSeconds, double firstBeatSeconds = 0)
    {
        if (!IsUsable(bpm) || !IsUsable(durationSeconds))
            return Array.Empty<double>();

        double beatSeconds = SecondsPerMinute / bpm;
        if (!IsUsable(beatSeconds))
            return Array.Empty<double>();

        // Anchor on the first beat when it is a sane in-track offset; otherwise grid from the start.
        double anchor =
            firstBeatSeconds > 0 && !double.IsNaN(firstBeatSeconds) && !double.IsInfinity(firstBeatSeconds)
            && firstBeatSeconds < durationSeconds
                ? firstBeatSeconds
                : 0.0;

        // Lines at anchor, anchor+beatSeconds, … up to (not past) the track end. Index 0 = the first beat.
        var fractions = new List<double>();
        for (int beat = 0; beat <= MaxBeatLines; beat++)
        {
            double t = anchor + (beat * beatSeconds);
            if (t > durationSeconds)
                break;
            fractions.Add(t / durationSeconds);
        }

        return fractions;
    }

    private static bool IsUsable(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
