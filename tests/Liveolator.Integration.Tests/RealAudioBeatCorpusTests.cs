using System.Text;
using System.Text.Json;
using Liveolator.Audio;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Xunit;
using Xunit.Abstractions;

namespace Liveolator.Integration.Tests;

/// <summary>
/// Scores the offline beat detector against REAL annotated tracks (see tests/corpus/README.md). This is the
/// gating measurement for the beat-sync work: drop a few tracks + ground-truth BPM/beat-phase into
/// tests/corpus (or $LIVEOLATOR_CORPUS_DIR) and this reports detected-vs-truth and asserts each is in
/// tolerance. With no corpus present it is a no-op, so CI stays green.
/// </summary>
public sealed class RealAudioBeatCorpusTests
{
    private readonly ITestOutputHelper _out;
    public RealAudioBeatCorpusTests(ITestOutputHelper output) => _out = output;

    private const double BpmToleranceBpm = 3.0;
    private const double PhaseToleranceMs = 50.0;

    [Fact]
    public async Task DetectsRealTracks_WithinTolerance()
    {
        string? dir = ResolveCorpusDir();
        string? annotationsPath = dir is null ? null : Path.Combine(dir, "annotations.json");
        if (annotationsPath is null || !File.Exists(annotationsPath))
        {
            _out.WriteLine("No tests/corpus/annotations.json — real-audio corpus is empty, skipping. " +
                           "See tests/corpus/README.md to add tracks.");
            return;
        }

        Annotation[] annotations =
            JsonSerializer.Deserialize<Annotation[]>(
                await File.ReadAllTextAsync(annotationsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? Array.Empty<Annotation>();

        var report = new StringBuilder($"Real-audio corpus: {annotations.Length} track(s) in {dir}\n");
        var failures = new List<string>();
        foreach (Annotation a in annotations)
        {
            string path = Path.Combine(dir!, a.File);
            if (!File.Exists(path)) { failures.Add($"{a.File}: file not found"); continue; }

            float[] mono;
            try
            {
                mono = await DecodeMonoAsync(path);
            }
            catch (Exception ex)
            {
                // A non-WAV track needs FFmpeg on PATH; report rather than fail the whole run if it's absent.
                report.AppendLine($"  {a.File}: decode skipped ({ex.GetType().Name}: {ex.Message})");
                continue;
            }

            BpmResult r = new BpmDetector().Detect(mono, TrackAnalyzer.AnalysisSampleRate);
            (bool tempoOk, bool octave) = TempoMatches(r.Bpm, a.Bpm);
            double period = r.Bpm > 0 ? 60.0 / r.Bpm : 0.0;
            double phaseMs = period > 0 && !octave
                ? CircularDistance(r.FirstBeatSeconds, a.FirstBeatSeconds % period, period) * 1000.0
                : double.NaN;

            string verdict = tempoOk && (double.IsNaN(phaseMs) || phaseMs <= PhaseToleranceMs) ? "OK" : "MISS";
            report.AppendLine(
                $"  [{verdict}] {a.File}: {r.Bpm:F1} bpm (truth {a.Bpm:0}{(octave ? ", OCTAVE" : "")}), " +
                $"phase {(double.IsNaN(phaseMs) ? "n/a" : $"{phaseMs:F1}ms")}");
            if (verdict == "MISS")
                failures.Add($"{a.File}: detected {r.Bpm:F1} vs {a.Bpm:0}, phase " +
                             $"{(double.IsNaN(phaseMs) ? "n/a" : $"{phaseMs:F1}ms")}");
        }

        _out.WriteLine(report.ToString());
        Assert.True(failures.Count == 0, "Real-audio corpus misses:\n" + string.Join("\n", failures));
    }

    private static async Task<float[]> DecodeMonoAsync(string path)
    {
        IAudioDecoder decoder = new WavAudioDecoder().CanDecode(path)
            ? new WavAudioDecoder()
            : new FfmpegAudioDecoder();
        var samples = new List<float>();
        await foreach (ReadOnlyMemory<float> block in
            decoder.DecodeMonoAsync(path, TrackAnalyzer.AnalysisSampleRate))
            samples.AddRange(block.ToArray());
        return samples.ToArray();
    }

    // tests/corpus relative to the repo, or $LIVEOLATOR_CORPUS_DIR. Walk up from the test binary to find it.
    private static string? ResolveCorpusDir()
    {
        string? env = Environment.GetEnvironmentVariable("LIVEOLATOR_CORPUS_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir is not null; up++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "corpus");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static (bool ok, bool octave) TempoMatches(double detected, double truth)
    {
        if (detected <= 0) return (false, false);
        if (Math.Abs(detected - truth) <= BpmToleranceBpm) return (true, false);
        bool octave = Math.Abs(detected - truth * 2) <= BpmToleranceBpm
                      || Math.Abs(detected - truth / 2) <= BpmToleranceBpm;
        return (false, octave);
    }

    private static double CircularDistance(double a, double b, double period)
    {
        double d = Math.Abs(a - b) % period;
        return Math.Min(d, period - d);
    }

    private sealed record Annotation(string File, double Bpm, double FirstBeatSeconds, double? DownbeatSeconds);
}
