using System.Diagnostics;
using System.Runtime.InteropServices;
using Liveolator.Audio;
using Liveolator.Core.Analysis.Stems;
using Liveolator.Media.Analysis;
using Xunit;

namespace Liveolator.Integration.Tests;

/// <summary>
/// Real-runtime test for the Open-Unmix stem separator (doc 32 Phase 2). It runs ONLY when a Python
/// interpreter with openunmix importable is resolvable on PATH; otherwise it skips, exactly like the
/// structure-analysis integration test. Its absence never fails the suite (download-on-demand, §2.1).
/// </summary>
public class OpenUnmixStemSeparatorTests
{
    [Fact]
    public async Task SeparateAsync_RealOpenUnmix_ReturnsFourStems()
    {
        string? prefix = ResolvePythonPrefixWith("openunmix");
        if (prefix is null)
        {
            Assert.True(true, "Python+openunmix not resolvable — stem separation integration test skipped (doc 32).");
            return;
        }

        using var dir = new TempDir();
        // A short stereo tone is enough to exercise the full separate-write-manifest pipeline.
        float[] tone = TestMedia.ClickTrain(120, 44100, 4);
        string wavPath = dir.Write("fixture.wav", TestMedia.Pcm16Wav(new[] { tone, tone }, 44100));

        string scriptPath = Path.Combine(
            Path.GetDirectoryName(typeof(OpenUnmixStemSeparator).Assembly.Location)!,
            "scripts", "separate_stems.py");

        var store = new StemStore(Path.Combine(dir.Path, "cache"));
        var separator = new OpenUnmixStemSeparator(new PythonRuntime(baseDir: prefix), store, scriptPath);
        var decoder = new WavAudioDecoder();

        StemSet? stems = await separator.SeparateAsync(decoder, wavPath);

        Assert.NotNull(stems);
        Assert.True(stems!.IsComplete);
        Assert.Equal("umxhq", stems.ModelId);
        foreach (string path in stems.StemPaths.Values)
            Assert.True(File.Exists(path), $"expected stem file '{path}' on disk");

        // Second call must be a cache hit (same set, no re-run).
        StemSet? cached = await separator.SeparateAsync(decoder, wavPath);
        Assert.NotNull(cached);
        Assert.True(cached!.IsComplete);
    }

    private static string? ResolvePythonPrefixWith(string module)
    {
        foreach (string exe in new[] { "python3", "python" })
        {
            string? prefix = RunForOutput(exe, "-c", "import sys,os;print(os.path.dirname(sys.executable))");
            if (prefix is null || !Directory.Exists(prefix.Trim()))
                continue;

            string baseDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? prefix.Trim()
                : Path.GetDirectoryName(prefix.Trim()) ?? prefix.Trim();

            if (RunForOutput(exe, "-c", "import " + module) is not null)
                return baseDir;
        }
        return null;
    }

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
            p.WaitForExit(120_000);
            return p.HasExited && p.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
