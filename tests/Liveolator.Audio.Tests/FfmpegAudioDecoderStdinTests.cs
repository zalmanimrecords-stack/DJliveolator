using System.Diagnostics;

namespace Liveolator.Audio.Tests;

/// <summary>
/// Guards the invariant that deadlocked MCP library scans: a child FFmpeg must never inherit this
/// process's stdin. In the <c>--stdio</c> MCP server that handle IS the JSON-RPC transport, so an
/// FFmpeg polling stdin for interactive keys steals the protocol stream and the scan hangs forever
/// at 0% CPU. Asserted on the built <see cref="ProcessStartInfo"/> so the rule holds on a machine
/// (and in CI) where FFmpeg is absent; the subprocess path itself is an integration test.
/// </summary>
public sealed class FfmpegAudioDecoderStdinTests
{
    [Fact]
    public void BuildStartInfo_RedirectsStandardInput_SoTheChildCannotInheritOurs()
    {
        ProcessStartInfo psi = FfmpegAudioDecoder.BuildStartInfo("ffmpeg", "track.flac", 44_100);

        Assert.True(psi.RedirectStandardInput);
    }

    [Fact]
    public void BuildStartInfo_StillPipesMonoFloatPcmAtTheRequestedRate()
    {
        ProcessStartInfo psi = FfmpegAudioDecoder.BuildStartInfo("/opt/ffmpeg", "track.flac", 16_000);

        Assert.Equal("/opt/ffmpeg", psi.FileName);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.False(psi.UseShellExecute);
        Assert.Contains("track.flac", psi.ArgumentList);
        Assert.Contains("f32le", psi.ArgumentList);
        Assert.Contains("16000", psi.ArgumentList);
        Assert.Contains("pipe:1", psi.ArgumentList);
    }
}
