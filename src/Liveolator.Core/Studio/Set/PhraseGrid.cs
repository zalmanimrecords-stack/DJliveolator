using Liveolator.Core.Analysis.Bpm;

namespace Liveolator.Core.Studio.Set;

/// <summary>
/// A track's own phrase grid: the 16-bar lines measured from its analyzed downbeat. Every mix point is
/// quantized onto it, and that single rule is what makes the arrangement phase-correct.
/// <para>The reason: a clip whose <c>SourceIn</c> is one of these lines and whose start sits on a project
/// phrase line has <em>every</em> later phrase land on a project phrase line too — warping to the project
/// tempo turns the track's phrase length into the project's phrase length exactly. So quantizing both ends
/// of a transition is sufficient for the two tracks to stay phrase-aligned for the whole crossfade, with
/// no per-transition correction.</para>
/// </summary>
public readonly record struct PhraseGrid(double DownbeatSeconds, double PhraseSeconds)
{
    /// <summary>A grid with no usable tempo; every quantization is the identity.</summary>
    public static PhraseGrid None { get; } = new(0.0, 0.0);

    /// <summary>True when the track had a usable tempo to build a grid from.</summary>
    public bool HasTempo => PhraseSeconds > 0.0;

    /// <summary>The track's phrase grid, or <see cref="None"/> when its tempo is unknown.</summary>
    public static PhraseGrid For(BpmResult? bpm, int phraseBars = SetBuildOptions.PhraseBars)
    {
        if (bpm is null || bpm.Bpm <= 0.0)
            return None;

        BeatGrid grid = BeatGrid.FromBpmResult(bpm);
        return new PhraseGrid(grid.DownbeatSeconds, grid.BarSeconds * phraseBars);
    }

    /// <summary>The phrase line at or before <paramref name="seconds"/> (never negative).</summary>
    public double Floor(double seconds) => Snap(seconds, Math.Floor);

    /// <summary>The phrase line at or after <paramref name="seconds"/>.</summary>
    public double Ceiling(double seconds) => Snap(seconds, Math.Ceiling);

    /// <summary>The phrase line nearest <paramref name="seconds"/>.</summary>
    public double Nearest(double seconds) => Snap(seconds, Math.Round);

    // Quantizes onto the grid, then lifts a result that fell before the track start up to the first
    // non-negative line — a downbeat anchor near t=0 must never produce a negative source position.
    private double Snap(double seconds, Func<double, double> rounding)
    {
        if (!HasTempo)
            return Math.Max(0.0, seconds);

        double phrases = rounding((seconds - DownbeatSeconds) / PhraseSeconds);
        double snapped = DownbeatSeconds + (phrases * PhraseSeconds);
        while (snapped < 0.0)
            snapped += PhraseSeconds;
        return snapped;
    }
}
