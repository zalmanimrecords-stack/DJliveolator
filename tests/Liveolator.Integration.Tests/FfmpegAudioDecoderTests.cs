using System.Diagnostics;
using Liveolator.Audio;
using Xunit;

namespace Liveolator.Integration.Tests;

/// <summary>
/// Tests for the FFmpeg CLI-subprocess decoder. The extension matrix, argument validation, and
/// missing-executable tests are always runnable. The real-decode test needs the FFmpeg CLI on
/// PATH (used both to build an encoded fixture and as the decoder backend); it skips gracefully
/// when FFmpeg is absent so its absence never fails the suite.
/// </summary>
public class FfmpegAudioDecoderTests
{
    [Theory]
    [InlineData("track.mp3", true)]
    [InlineData("track.MP3", true)]
    [InlineData("track.flac", true)]
    [InlineData("track.m4a", true)]
    [InlineData("track.aac", true)]
    [InlineData("track.ogg", true)]
    [InlineData("track.opus", true)]
    [InlineData("track.wav", false)]   // WAV is WavAudioDecoder's responsibility
    [InlineData("track.txt", false)]
    [InlineData("track", false)]
    public void CanDecode_ByExtension(string path, bool expected)
        => Assert.Equal(expected, new FfmpegAudioDecoder().CanDecode(path));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DecodeMonoAsync_NonPositiveSampleRate_Throws(int rate)
    {
        var decoder = new FfmpegAudioDecoder();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in decoder.DecodeMonoAsync("anything.mp3", rate)) { }
        });
    }

    [Fact]
    public async Task DecodeMonoAsync_MissingExecutable_ThrowsActionableError()
    {
        var decoder = new FfmpegAudioDecoder("liveolator-no-such-ffmpeg-binary");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in decoder.DecodeMonoAsync("anything.mp3", 44100)) { }
        });
    }

    [Fact]
    public async Task DecodeMonoAsync_RealEncodedFile_DecodesMonoPcm()
    {
        if (!FfmpegCliAvailable())
        {
            Assert.True(true, "FFmpeg CLI not on PATH — real-decode test skipped (see docs/01).");
            return;
        }

        const int sampleRate = 44100;
        using var dir = new TempDir();
        string wavPath = dir.Write("seed.wav", TestMedia.Pcm16Wav(TestMedia.ClickTrain(120, sampleRate, 1), sampleRate));
        string flacPath = Path.Combine(dir.Path, "fixture.flac");
        Assert.True(RunFfmpeg("-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", wavPath, flacPath),
            "Failed to build the encoded fixture with the FFmpeg CLI.");

        var pcm = new List<float>();
        await foreach (var block in new FfmpegAudioDecoder().DecodeMonoAsync(flacPath, sampleRate))
            pcm.AddRange(block.ToArray());

        // ~1 second of mono audio at the target rate, with codec priming/padding slack.
        Assert.InRange(pcm.Count, (int)(sampleRate * 0.5), (int)(sampleRate * 1.5));
    }

    private static bool FfmpegCliAvailable() => RunFfmpeg("-version");

    private static bool RunFfmpeg(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args) psi.ArgumentList.Add(arg);

            using Process? process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false; // no ffmpeg CLI on PATH
        }
    }
}
