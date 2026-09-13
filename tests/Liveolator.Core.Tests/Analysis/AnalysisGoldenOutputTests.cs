using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Tests.Analysis.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Liveolator.Core.Tests.Analysis;

/// <summary>
/// Pins the offline analyser's output, value for value, against a committed baseline.
/// </summary>
/// <remarks>
/// <para>
/// This exists to make one specific class of change safe: speeding the analyser up without altering what
/// it concludes. A beat grid that shifts is worse than a slow scan — it is silent, it is persisted to the
/// catalog, and a DJ discovers it when two decks refuse to lock. The accuracy tests next door ask "is the
/// detector good enough" against tolerances; this one asks the different question "did the numbers move at
/// all", which is the only question a performance refactor is allowed to answer with no.
/// </para>
/// <para>
/// The corpus is <see cref="BeatDetectionCorpus"/>: synthetic, deterministic, generated in-process, so the
/// baseline is reproducible on any machine and needs no audio files. If a change here is deliberate — a
/// genuine improvement to detection — regenerate the baseline by setting the environment variable
/// <c>LIVEOLATOR_UPDATE_GOLDEN=1</c>, and say in the commit why the numbers moved. Never regenerate to make
/// a red test green.
/// </para>
/// </remarks>
public sealed class AnalysisGoldenOutputTests
{
    private const string UpdateVariable = "LIVEOLATOR_UPDATE_GOLDEN";

    private readonly ITestOutputHelper _output;

    public AnalysisGoldenOutputTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Analysis_output_matches_the_committed_baseline()
    {
        string actual = RenderReport();
        string baselinePath = BaselinePath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, actual);
            _output.WriteLine($"Baseline rewritten: {baselinePath}");
            return;
        }

        Assert.True(File.Exists(baselinePath),
            $"No baseline at '{baselinePath}'. Generate it with {UpdateVariable}=1 and commit it.");

        string expected = File.ReadAllText(baselinePath);
        if (string.Equals(Normalise(expected), Normalise(actual), StringComparison.Ordinal))
            return;

        // A diff of thirty lines is unreadable in an assertion message; name the rows that moved.
        string[] expectedLines = Normalise(expected).Split('\n');
        string[] actualLines = Normalise(actual).Split('\n');
        var drift = new List<string>();
        for (int i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            string e = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            string a = i < actualLines.Length ? actualLines[i] : "<missing>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
                drift.Add($"  expected: {e}\n  actual  : {a}");
        }

        Assert.Fail(
            $"The analyser's output changed on {drift.Count} row(s). A performance change must not move "
            + $"these numbers; if the change was deliberate, regenerate with {UpdateVariable}=1 and explain "
            + $"it in the commit.\n\n{string.Join("\n\n", drift.Take(12))}");
    }

    // One line per corpus case, every field the catalog would persist from a BpmResult. Fixed-precision and
    // invariant-culture so the baseline is byte-stable across machines and locales.
    private static string RenderReport()
    {
        var analyzer = new TrackAnalyzer();
        var report = new StringBuilder();
        report.Append("# Liveolator analysis golden output — see AnalysisGoldenOutputTests\n");
        report.Append("# case | bpm | conf | firstBeat | downbeat | dbConf | bar | kicks "
            + "| coherence | stability | key | cues\n");

        foreach (CorpusCase c in BeatDetectionCorpus.Cases.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            float[] pcm = BeatDetectionCorpus.Render(c);
            TrackAnalysisResult result = analyzer.AnalyzePcm(pcm, BeatDetectionCorpus.SampleRate);
            BpmResult bpm = result.Bpm;

            // Nullable doubles print as "null" rather than an empty column, so a value appearing or
            // disappearing is as visible as a value changing.
            string coherence = bpm.GridCoherence is { } gc
                ? gc.ToString("F4", CultureInfo.InvariantCulture) : "null";
            string stability = bpm.TempoStabilityBpmDelta is { } ts
                ? ts.ToString("F4", CultureInfo.InvariantCulture) : "null";

            report.Append(string.Create(CultureInfo.InvariantCulture,
                $"{c.Name} | {bpm.Bpm:F4} | {bpm.Confidence:F4} | {bpm.FirstBeatSeconds:F4} | "));
            report.Append(string.Create(CultureInfo.InvariantCulture,
                $"{bpm.DownbeatSeconds:F4} | {bpm.DownbeatConfidence:F4} | {bpm.BeatsPerBar} | "));
            report.Append(string.Create(CultureInfo.InvariantCulture,
                $"{bpm.KickOnsetsSeconds.Count} | {coherence} | {stability} | "));
            report.Append(string.Create(CultureInfo.InvariantCulture,
                $"{result.Key} | {Cue(result.Cues.IntroStart)},{Cue(result.Cues.IntroEnd)},"));
            report.Append(string.Create(CultureInfo.InvariantCulture,
                $"{Cue(result.Cues.OutroStart)},{Cue(result.Cues.OutroEnd)}\n"));
        }

        return report.ToString();
    }

    // Cue points are nullable by design (intro/outro ends need phrase analysis that does not exist yet),
    // so an absent one prints as "-" and a cue appearing is as visible as a cue moving.
    private static string Cue(TimeSpan? at) =>
        at is { } value ? value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) : "-";

    private static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    private static string BaselinePath()
    {
        // Walk up from the test binary to the repo so the baseline lives with the source, not in bin/.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Liveolator.sln")))
            directory = directory.Parent;

        Assert.True(directory is not null, "Could not locate the repository root from the test binary.");
        return Path.Combine(directory!.FullName, "tests", "Liveolator.Core.Tests", "Analysis",
            "analysis-golden-output.txt");
    }
}
