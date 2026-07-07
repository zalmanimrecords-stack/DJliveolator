using Liveolator.Core.Analysis.Bpm;

namespace Liveolator.Core.Tests.Analysis.Corpus;

/// <summary>One labelled beat-detection case: a synthetic mix with known ground-truth tempo and beat phase.</summary>
/// <param name="Name">Human-readable label for the report.</param>
/// <param name="Bpm">Ground-truth tempo.</param>
/// <param name="KickOffsetSeconds">Ground-truth first-beat (kick) offset from track start.</param>
/// <param name="BassHz">Off-beat bass frequency — &gt; ~200 Hz pollutes only the broadband phase; ≤ 200 Hz also pollutes the kick band.</param>
/// <param name="Pollution">Tag for grouping in the report.</param>
internal sealed record CorpusCase(string Name, double Bpm, double KickOffsetSeconds, double BassHz, string Pollution);

/// <summary>
/// A synthetic-but-realistic beat-detection corpus with ground truth, plus the accuracy evaluator. It is the
/// objective yardstick the system review (2026-06-27) called for: synthetic single-spike tests can't reveal
/// bass pollution or octave errors. Every case is a four-on-the-floor kick (the beat) with competing off-beat
/// energy, so a detector's tempo and PHASE can be scored against truth. The same evaluator scores real audio
/// (<c>RealAudioBeatCorpus</c>) once annotated tracks are supplied — see tests/corpus/README.
/// </summary>
internal static class BeatDetectionCorpus
{
    public const int SampleRate = 44_100;
    public const double Seconds = 16.0;

    /// <summary>Tempos spanning house/techno/trance/DnB so octave (half/double) traps are represented.
    /// 86 probes the grid-refiner band boundary (its double 172 is now in-band); 168–178 are the fast
    /// tempos the pipeline used to fold down to ~70/87.</summary>
    private static readonly double[] Tempos = { 86, 90, 100, 110, 120, 124, 128, 130, 140, 150, 168, 172, 174, 178 };

    /// <summary>Fast tempos also rendered as DnB half-time (kick on 1, snare on 3, dense hats) — the
    /// pattern whose kick–snare backbone makes 174 read as ~70/87.</summary>
    private static readonly double[] DnbTempos = { 168, 172, 174, 178 };

    private const string DnbPollution = "dnb-halftime";

    /// <summary>The corpus: each tempo at a non-trivial phase offset, in both pollution regimes.</summary>
    public static IReadOnlyList<CorpusCase> Cases { get; } = Build();

    private static CorpusCase[] Build()
    {
        var cases = new List<CorpusCase>();
        foreach (double bpm in Tempos)
        {
            double offset = 0.08 + bpm % 3 * 0.02; // a deterministic, varied non-zero beat phase per case
            cases.Add(new CorpusCase($"{bpm:0} bpm / bass>band", bpm, offset, BassHz: 320.0, "broadband-only"));
            cases.Add(new CorpusCase($"{bpm:0} bpm / bass in-band", bpm, offset, BassHz: 110.0, "in-band-bass"));
        }
        foreach (double bpm in DnbTempos)
        {
            double offset = 0.08 + bpm % 3 * 0.02;
            cases.Add(new CorpusCase($"{bpm:0} bpm / dnb half-time", bpm, offset, BassHz: 0.0, DnbPollution));
        }
        return cases.ToArray();
    }

    public static float[] Render(CorpusCase c) => c.Pollution == DnbPollution
        ? BeatMixSignals.KickSnareHatsDnB(c.Bpm, SampleRate, Seconds, c.KickOffsetSeconds)
        : BeatMixSignals.KickBassHatsFourOnFloor(
            c.Bpm, SampleRate, Seconds, c.KickOffsetSeconds, bassHz: c.BassHz);

    /// <summary>Score one detection against ground truth (octave-aware tempo, circular phase error in ms).</summary>
    public static CaseScore Score(CorpusCase c, BpmResult result)
    {
        bool tempoOk = TempoMatches(result.Bpm, c.Bpm, out bool octaveOff);
        double period = result.Bpm > 0 ? 60.0 / result.Bpm : 0.0;
        // Phase is only meaningful when the tempo (period) is right; an octave error makes it incomparable.
        double phaseErrMs = period > 0 && !octaveOff
            ? BeatMixSignals.CircularDistanceSeconds(result.FirstBeatSeconds, c.KickOffsetSeconds % period, period) * 1000.0
            : double.NaN;
        return new CaseScore(c, result.Bpm, tempoOk, octaveOff, phaseErrMs);
    }

    // Tempo is correct if within 2 BPM of truth. If it instead matches a half/double of truth, flag octaveOff
    // (a wrong metrical level — the failure mode the corpus exists to catch) rather than a near miss.
    private static bool TempoMatches(double detected, double truth, out bool octaveOff)
    {
        octaveOff = false;
        if (detected <= 0) return false;
        if (Math.Abs(detected - truth) <= 2.0) return true;
        if (Math.Abs(detected - truth * 2) <= 2.0 || Math.Abs(detected - truth / 2) <= 2.0)
        {
            octaveOff = true;
        }
        return false;
    }
}

/// <summary>The score for one case: detected tempo, whether tempo/phase landed, and the phase error (ms).</summary>
internal sealed record CaseScore(
    CorpusCase Case, double DetectedBpm, bool TempoOk, bool OctaveOff, double PhaseErrorMs);
