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

    /// <summary>
    /// Beat-line positions as 0..1 fractions of the track, evenly spaced at the tempo's beat interval
    /// from the track start. The first line (the start, fraction 0) is included.
    /// </summary>
    /// <param name="bpm">Track tempo in beats per minute; 0 or negative means "unknown" → empty grid.</param>
    /// <param name="durationSeconds">Track length in seconds; 0 or negative → empty grid.</param>
    public static IReadOnlyList<double> BeatFractions(double bpm, double durationSeconds)
    {
        if (!IsUsable(bpm) || !IsUsable(durationSeconds))
            return Array.Empty<double>();

        double beatSeconds = SecondsPerMinute / bpm;
        if (!IsUsable(beatSeconds))
            return Array.Empty<double>();

        // Lines at 0, beatSeconds, 2·beatSeconds … up to (not past) the track end.
        int beatCount = (int)Math.Floor(durationSeconds / beatSeconds);
        if (beatCount < 0)
            return Array.Empty<double>();
        if (beatCount > MaxBeatLines)
            beatCount = MaxBeatLines;

        var fractions = new List<double>(beatCount + 1);
        for (int beat = 0; beat <= beatCount; beat++)
        {
            double fraction = (beat * beatSeconds) / durationSeconds;
            if (fraction > 1.0)
                break;
            fractions.Add(fraction);
        }

        return fractions;
    }

    private static bool IsUsable(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
