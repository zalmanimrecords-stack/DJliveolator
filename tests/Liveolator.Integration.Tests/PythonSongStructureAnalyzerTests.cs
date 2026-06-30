using System.Diagnostics;
using System.Runtime.InteropServices;
using Liveolator.Audio;
using Liveolator.Media.Analysis;
using Xunit;

namespace Liveolator.Integration.Tests;

/// <summary>
/// Real-runtime test for the Python/librosa structure analyzer (doc 32). It runs ONLY when a Python
/// interpreter with librosa importable is resolvable on PATH; otherwise it skips, exactly like the
/// real-FFmpeg decode test. Its absence never fails the suite (download-on-demand, §2.1).
/// </summary>
public class PythonSongStructureAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_RealLibrosa_ReturnsSections()
    {
        string? prefix = ResolvePythonPrefixWithLibrosa();
        if (prefix is null)
        {
            Assert.True(true, "Python+librosa not resolvable — structure analysis integration test skipped (doc 32).");
            return;
        }

        using var dir = new TempDir();
        // ~30 s click track so the segmenter has multiple beats/phrases to cut on.
        string wavPath = dir.Write("fixture.wav", TestMedia.Pcm16Wav(TestMedia.ClickTrain(128, 22050, 30), 22050));

        string scriptPath = Path.Combine(
            Path.GetDirectoryName(typeof(PythonSongStructureAnalyzer).Assembly.Location)!,
            "scripts", "analyze_structure.py");

        var analyzer = new PythonSongStructureAnalyzer(new PythonRuntime(baseDir: prefix), scriptPath);
        var decoder = new WavAudioDecoder();

        var structure = await analyzer.AnalyzeAsync(decoder, wavPath);

        Assert.NotNull(structure);
        Assert.NotEmpty(structure!.Sections);
        Assert.Contains("librosa", structure.AnalyzedWith);
        Assert.Equal(0.0, structure.Ordered[0].StartSeconds); // first section anchors at track start
    }

    /// <summary>
    /// Returns the prefix dir such that PythonRuntime(baseDir).InterpreterPath is the real interpreter,
    /// but only if librosa imports there; otherwise null (skip).
    /// </summary>
    private static string? ResolvePythonPrefixWithLibrosa()
    {
        foreach (string exe in new[] { "python3", "python" })
        {
            string? prefix = RunForOutput(exe, "-c", "import sys,os;print(os.path.dirname(sys.executable))");
            if (prefix is null || !Directory.Exists(prefix.Trim()))
                continue;

            // On non-Windows the interpreter sits in <prefix>/bin; PythonRuntime expects baseDir/bin/python3,
            // so the baseDir is the parent of the bin dir.
            string baseDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? prefix.Trim()
                : Path.GetDirectoryName(prefix.Trim()) ?? prefix.Trim();

            if (RunForOutput(exe, "-c", "import librosa") is not null)
                return baseDir;
        }
        return null;
    }

    /// <summary>Runs a command; returns trimmed stdout on exit 0, else null (missing binary / import error).</summary>
    private static string? RunForOutput(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args) psi.ArgumentList.Add(a);

            using Process? p = Process.Start(psi);
            if (p is null) return null;
            string stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(60_000);
            return p.HasExited && p.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
