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
                report.AppendLine(
                    $"  {r.Bpm,7:F2} bpm | coh {(r.GridCoherence is { } gc ? gc.ToString("F3") : " null")} " +
                    $"| stab {(r.TempoStabilityBpmDelta is { } sd ? sd.ToString("F3") : " null")} " +
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
