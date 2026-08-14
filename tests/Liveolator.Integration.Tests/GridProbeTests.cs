using System.Text;
using Liveolator.Audio;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Xunit;
using Xunit.Abstractions;

namespace Liveolator.Integration.Tests;

/// <summary>
/// TEMPORARY diagnostic probe (2026-08-02): runs the CURRENT analyzer over real tracks in
/// $LIVEOLATOR_PROBE_DIR and prints tempo, confidence and grid coherence. Read-only — it never touches
/// the catalog. Delete once the beat-grid question is settled.
/// </summary>
public sealed class GridProbeTests
{
    private readonly ITestOutputHelper _out;
    public GridProbeTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task ProbeRealTracks()
    {
        string? dir = Environment.GetEnvironmentVariable("LIVEOLATOR_PROBE_DIR");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            _out.WriteLine("LIVEOLATOR_PROBE_DIR not set — nothing to probe.");
            return;
        }

        int max = int.TryParse(Environment.GetEnvironmentVariable("LIVEOLATOR_PROBE_MAX"), out int m) ? m : 6;
        string[] files = Directory
            .EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();

        var report = new StringBuilder($"analyzer v{TrackAnalyzer.CurrentVersion} over {files.Length} track(s)\n");
        foreach (string path in files)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                float[] mono = await DecodeMonoAsync(path);
                BpmResult r = new BpmDetector().Detect(mono, TrackAnalyzer.AnalysisSampleRate);
                GridConfidence g = GridConfidenceCalculator.Evaluate(r);
                (double shippedAnchor, double shippedMargin) = ShippedAnchor(mono, r.Bpm);
                double correctionMs = 1000.0 * CircularDistance(shippedAnchor, r.FirstBeatSeconds, 60.0 / r.Bpm);
                report.AppendLine(
                    $"      v11 anchor {shippedAnchor,6:F3} (margin {shippedMargin,5:F2}) " +
                    $"→ v12 moved it {correctionMs,6:F1} ms");
                report.AppendLine(
                    $"  {r.Bpm,7:F2} bpm | coh {(r.GridCoherence is { } gc ? gc.ToString("F3") : " null")} " +
                    $"| stab {(r.TempoStabilityBpmDelta is { } sd ? sd.ToString("F3") : " null")} " +
                    $"| first {r.FirstBeatSeconds,6:F3} " +
                    $"| margin {(r.KickPhaseMarginRatio is { } mr ? mr.ToString("F2") : " null"),6} " +
                    $"| drift {(r.PhaseWindowDisagreementSeconds is { } wd ? (wd * 1000).ToString("F1") : "null"),6} ms " +
                    $"| display {(g.Display is { } dv ? dv.ToString("F2") : "null")} " +
                    $"| PHASE-SYNC {(g.PhaseSyncReady ? "yes" : "NO ")} | {name}");
            }
            catch (Exception ex)
            {
                report.AppendLine($"  decode/analyze failed: {name} ({ex.GetType().Name}: {ex.Message})");
            }
        }

        _out.WriteLine(report.ToString());
    }

    // What v11 published as the beat anchor: the HPSS kick fit's resultant phase. Reproduced here (rather
    // than read from the catalog) so the same run measures both anchors on the same decode, plus the
    // kick-identity margin AT the old anchor — i.e. whether the new gate would have caught it.
    private static (double AnchorSeconds, double MarginRatio) ShippedAnchor(float[] mono, double bpm)
    {
        int sr = TrackAnalyzer.AnalysisSampleRate;
        var onset = new OnsetEnvelope();
        double coarse = new TempoEstimator().Estimate(onset.Compute(mono), onset.EnvelopeRateHz(sr)).Bpm;
        var hpss = new PercussiveOnsetEnvelope();
        double[] kick = hpss.Compute(mono, sr);
        GridFit fit = new GridRefiner().Refine(kick, hpss.EnvelopeRateHz(sr), coarse, 0.0);
        double period = 60.0 / bpm;
        double anchor = (fit.FirstBeatSeconds + hpss.AnalysisLatencySeconds(sr)) % period;
        if (anchor < 0.0) anchor += period;

        var bands = new Liveolator.Core.Analysis.Cues.BandEnergyEnvelope().Compute(mono, sr);
        double margin = KickPhaseGate.MarginRatio(
            KickPhaseGate.BeatProfile(bands.Low, bands.FrameRateHz, bpm), bpm, anchor);
        return (Math.Round(anchor, 4), margin);
    }

    private static double CircularDistance(double a, double b, double period)
    {
        double d = Math.Abs(a - b) % period;
        return Math.Min(d, period - d);
    }

    private static async Task<float[]> DecodeMonoAsync(string path)
    {
        // The decoder does not read LIVEOLATOR_FFMPEG_PATH itself — the composition root does — so the
        // probe resolves it the same way before falling back to the bare name.
        IAudioDecoder decoder = new WavAudioDecoder().CanDecode(path)
            ? new WavAudioDecoder()
            : new FfmpegAudioDecoder(Environment.GetEnvironmentVariable("LIVEOLATOR_FFMPEG_PATH"));
        var samples = new List<float>();
        await foreach (ReadOnlyMemory<float> block in
            decoder.DecodeMonoAsync(path, TrackAnalyzer.AnalysisSampleRate))
            samples.AddRange(block.ToArray());
        return samples.ToArray();
    }
}
