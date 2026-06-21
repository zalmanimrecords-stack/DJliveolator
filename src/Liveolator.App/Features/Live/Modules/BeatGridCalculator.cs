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

    /// <summary>
    /// Which beat of the bar the grid (anchored on <paramref name="firstBeatSeconds"/>, index 0) starts on,
    /// given the track's downbeat (the musical "one"): the 0..<paramref name="beatsPerBar"/>-1 index offset
    /// the strip uses to mark a comb line as a bar downbeat — line <c>i</c> is a downbeat when
    /// <c>((i - offset) mod beatsPerBar) == 0</c>. Because the grid is anchored on the first BEAT (beat
    /// phase) while the downbeat is the BAR phase, the two need not coincide; this folds their difference
    /// into a beat count. Pure so the bar-marker placement unit-tests without a render.
    /// </summary>
    /// <param name="bpm">Track tempo; 0/negative/NaN → 0 (no usable beat interval).</param>
    /// <param name="firstBeatSeconds">The grid's beat-phase anchor (index 0), in seconds.</param>
    /// <param name="downbeatSeconds">The analyzed/edited downbeat (bar-1) offset in seconds; 0 or negative
    /// means "no downbeat known" → offset 0, so index 0 is treated as the bar start (the prior behaviour).</param>
    /// <param name="beatsPerBar">Meter; 4 for 4/4.</param>
    public static int DownbeatBarOffset(
        double bpm, double firstBeatSeconds, double downbeatSeconds, int beatsPerBar = BeatsPerBar)
    {
        if (!IsUsable(bpm) || beatsPerBar < 1 || downbeatSeconds <= 0
            || double.IsNaN(downbeatSeconds) || double.IsInfinity(downbeatSeconds)
            || double.IsNaN(firstBeatSeconds) || double.IsInfinity(firstBeatSeconds))
            return 0;

        double beatSeconds = SecondsPerMinute / bpm;
        if (!IsUsable(beatSeconds))
            return 0;

        // How many beats the downbeat sits from index 0, folded into one bar. Round so a downbeat that
        // lands a hair off a grid line still maps to the nearest beat (the line the strip actually draws).
        long beatsFromAnchor = (long)Math.Round((downbeatSeconds - firstBeatSeconds) / beatSeconds);
        int offset = (int)(((beatsFromAnchor % beatsPerBar) + beatsPerBar) % beatsPerBar);
        return offset;
    }

    private static bool IsUsable(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
