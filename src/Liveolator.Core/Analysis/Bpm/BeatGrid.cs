namespace Liveolator.Core.Analysis.Bpm;

/// <summary>
/// A track's constant-tempo beat grid: tempo plus the downbeat anchor and meter, with the phase math
/// the clock and deck sync read. Because the anchor is a real downbeat (beat 1 of the bar), bar-level
/// alignment — phrase-matched mixes, bar-quantized launches — composes for free, not just beat-level
/// alignment (doc 03). The grid is constant-tempo on purpose: DJ/electronic material is near-rigid, and
/// a single anchor + tempo defines every beat and bar without storing per-beat markers.
/// </summary>
/// <param name="Bpm">Tempo in BPM; 0 when undetectable.</param>
/// <param name="DownbeatSeconds">Offset of the first downbeat (beat 1) from track start, in seconds.</param>
/// <param name="BeatsPerBar">Meter; 4 for 4/4.</param>
/// <param name="Confidence">0..1 confidence in the downbeat placement (see <see cref="DownbeatEstimate"/>).</param>
public sealed record BeatGrid(double Bpm, double DownbeatSeconds, int BeatsPerBar, double Confidence)
{
    /// <summary>No grid — an unanalyzed or undetectable track. Phases read 0.</summary>
    public static readonly BeatGrid None = new(0.0, 0.0, 4, 0.0);

    /// <summary>True when a usable tempo was detected.</summary>
    public bool HasTempo => Bpm > 0.0;

    /// <summary>Seconds per beat (0 when there's no tempo).</summary>
    public double BeatSeconds => HasTempo ? 60.0 / Bpm : 0.0;

    /// <summary>Seconds per bar (0 when there's no tempo).</summary>
    public double BarSeconds => BeatSeconds * BeatsPerBar;

    /// <summary>Phase within the current beat, 0..1 (0 on every beat). 0 when there's no tempo.</summary>
    public double BeatPhaseAt(double seconds) => PhaseIn(seconds, BeatSeconds);

    /// <summary>Phase within the current bar, 0..1 (0 on every downbeat). 0 when there's no tempo.</summary>
    public double BarPhaseAt(double seconds) => PhaseIn(seconds, BarSeconds);

    /// <summary>The downbeat (bar boundary) nearest <paramref name="seconds"/>.</summary>
    public double NearestDownbeatTo(double seconds)
    {
        if (!HasTempo)
            return DownbeatSeconds;

        double bars = Math.Round((seconds - DownbeatSeconds) / BarSeconds);
        return DownbeatSeconds + bars * BarSeconds;
    }

    /// <summary>Projects the analysis result onto the runtime grid view.</summary>
    public static BeatGrid FromBpmResult(BpmResult bpm)
    {
        ArgumentNullException.ThrowIfNull(bpm);
        return new BeatGrid(bpm.Bpm, bpm.DownbeatSeconds, bpm.BeatsPerBar, bpm.DownbeatConfidence);
    }

    // Modulo that never returns a negative phase, so times before the anchor wrap cleanly into [0,1).
    private double PhaseIn(double seconds, double period)
    {
        if (period <= 0.0)
            return 0.0;

        double phase = ((seconds - DownbeatSeconds) / period) % 1.0;
        return phase < 0.0 ? phase + 1.0 : phase;
    }
}
