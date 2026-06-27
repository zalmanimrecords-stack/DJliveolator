using System.Text;
using Liveolator.Core.Analysis.Bpm;
using Xunit;
using Xunit.Abstractions;

namespace Liveolator.Core.Tests.Analysis.Corpus;

/// <summary>
/// Objective accuracy of the offline beat detector over the labelled synthetic corpus, comparing the
/// band-only kick onset against the percussive/HPSS one. Guards the phase-anchor + HPSS gains against
/// regression and prints a report the owner can read (xUnit output).
/// </summary>
public sealed class BeatDetectionAccuracyTests
{
    private readonly ITestOutputHelper _out;
    public BeatDetectionAccuracyTests(ITestOutputHelper output) => _out = output;

    private const double PhaseToleranceMs = 25.0;

    [Fact]
    public void Percussive_BeatsBandOnly_AndMeetsAccuracyBar()
    {
        DetectorRun band = Run("band-only", new LowBandOnsetEnvelope());
        DetectorRun perc = Run("percussive (HPSS)", new PercussiveOnsetEnvelope());

        _out.WriteLine(Report(band, perc));

        // The percussive detector must clear the accuracy bar across the whole corpus.
        Assert.True(perc.TempoOkRate >= 0.9, $"percussive tempo accuracy {perc.TempoOkRate:P0} < 90%");
        Assert.True(perc.PhaseOkRate >= 0.85, $"percussive phase accuracy {perc.PhaseOkRate:P0} < 85%");

        // And it must beat the band-only detector where they differ: the in-band-bass regime, the whole
        // reason percussive separation exists.
        double percInBand = perc.MeanPhaseErrorMs("in-band-bass");
        double bandInBand = band.MeanPhaseErrorMs("in-band-bass");
        Assert.True(
            percInBand < bandInBand,
            $"percussive should win on in-band bass: percussive {percInBand:F1}ms vs band {bandInBand:F1}ms");
    }

    private static DetectorRun Run(string label, IKickOnsetEnvelope kickOnset)
    {
        var detector = new BpmDetector(kickOnset: kickOnset);
        var scores = new List<CaseScore>();
        foreach (CorpusCase c in BeatDetectionCorpus.Cases)
        {
            BpmResult result = detector.Detect(BeatDetectionCorpus.Render(c), BeatDetectionCorpus.SampleRate);
            scores.Add(BeatDetectionCorpus.Score(c, result));
        }
        return new DetectorRun(label, scores);
    }

    private string Report(params DetectorRun[] runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Beat-detection corpus: {BeatDetectionCorpus.Cases.Count} cases " +
                      $"(phase tolerance {PhaseToleranceMs:0}ms)");
        foreach (DetectorRun r in runs)
        {
            sb.AppendLine($"  {r.Label,-20} tempo {r.TempoOkRate,5:P0} | phase {r.PhaseOkRate,5:P0} | " +
                          $"octave-errors {r.OctaveErrors} | mean-phase {r.MeanPhaseErrorMs():F1}ms " +
                          $"(in-band {r.MeanPhaseErrorMs("in-band-bass"):F1}ms)");
            foreach (CaseScore s in r.Misses())
                sb.AppendLine($"      miss [{r.Label}] {s.Case.Name}: detected {s.DetectedBpm:F1} bpm " +
                              $"(truth {s.Case.Bpm:0}{(s.OctaveOff ? ", octave" : "")}), " +
                              $"phase {(double.IsNaN(s.PhaseErrorMs) ? "n/a" : $"{s.PhaseErrorMs:F1}ms")}");
        }
        return sb.ToString();
    }

    private sealed class DetectorRun
    {
        public string Label { get; }
        private readonly IReadOnlyList<CaseScore> _scores;
        public DetectorRun(string label, IReadOnlyList<CaseScore> scores) { Label = label; _scores = scores; }

        public double TempoOkRate => _scores.Count(s => s.TempoOk) / (double)_scores.Count;
        public int OctaveErrors => _scores.Count(s => s.OctaveOff);
        public double PhaseOkRate =>
            _scores.Count(s => s.TempoOk && s.PhaseErrorMs <= PhaseToleranceMs) / (double)_scores.Count;

        // Cases that missed on tempo or phase — listed in the report so the owner can see what's hard.
        public IEnumerable<CaseScore> Misses() =>
            _scores.Where(s => !s.TempoOk || double.IsNaN(s.PhaseErrorMs) || s.PhaseErrorMs > PhaseToleranceMs);

        public double MeanPhaseErrorMs(string? pollution = null)
        {
            var errs = _scores
                .Where(s => s.TempoOk && !double.IsNaN(s.PhaseErrorMs) &&
                            (pollution is null || s.Case.Pollution == pollution))
                .Select(s => s.PhaseErrorMs)
                .ToList();
            return errs.Count > 0 ? errs.Average() : double.NaN;
        }
    }
}
