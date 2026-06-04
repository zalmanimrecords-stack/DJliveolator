using System.Diagnostics;
using Liveolator.Core.Library.Visual;
using Liveolator.Visuals;
using Xunit;

namespace Liveolator.Visuals.Tests;

public class VideoProbeTests
{
    // ---- CompositeVisualMediaProbe routing (deterministic, no ffprobe needed) ----

    [Fact]
    public async Task Composite_RoutesImageToImageProbe_AndVideoToVideoProbe()
    {
        var image = new RecordingProbe(new VisualMediaInfo(640, 480, null));
        var video = new RecordingProbe(new VisualMediaInfo(1920, 1080, TimeSpan.FromSeconds(5)));
        var composite = new CompositeVisualMediaProbe(image, video);

        VisualMediaInfo img = await composite.ProbeAsync("pic.png", VisualMediaKind.Image);
        VisualMediaInfo vid = await composite.ProbeAsync("clip.mp4", VisualMediaKind.Video);

        Assert.Equal(VisualMediaKind.Image, image.LastKind);
        Assert.Equal(VisualMediaKind.Video, video.LastKind);
        Assert.Equal(640, img.Width);
        Assert.Equal(1920, vid.Width);
        Assert.Equal(TimeSpan.FromSeconds(5), vid.Duration);
    }

    // ---- FfprobeVideoProbe guards (deterministic) ----

    [Fact]
    public async Task FfprobeVideoProbe_ImageKind_Throws()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => new FfprobeVideoProbe().ProbeAsync("pic.png", VisualMediaKind.Image));
    }

    [Fact]
    public async Task FfprobeVideoProbe_MissingExecutable_ThrowsActionableError()
    {
        var probe = new FfprobeVideoProbe("liveolator-no-such-ffprobe-binary");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => probe.ProbeAsync("clip.mp4", VisualMediaKind.Video));
    }

    // ---- Real ffprobe (guarded; skips when FFmpeg/ffprobe absent) ----

    [Fact]
    public async Task FfprobeVideoProbe_RealVideo_ReadsDimensionsAndDuration()
    {
        if (!OnPath("ffprobe") || !OnPath("ffmpeg"))
        {
            Assert.True(true, "ffmpeg/ffprobe not on PATH — real video-probe test skipped.");
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), "liveolator-vid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string clip = Path.Combine(dir, "clip.mp4");
            // Synthesize a 1s 320x240 test clip with the FFmpeg CLI.
            Assert.True(Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi",
                "-i", "testsrc=duration=1:size=320x240:rate=10", clip),
                "Failed to synthesize a test clip with ffmpeg.");

            VisualMediaInfo info = await new FfprobeVideoProbe().ProbeAsync(clip, VisualMediaKind.Video);

            Assert.Equal(320, info.Width);
            Assert.Equal(240, info.Height);
            Assert.NotNull(info.Duration);
            Assert.InRange(info.Duration!.Value.TotalSeconds, 0.7, 1.5);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static bool OnPath(string exe) => Run(exe, "-version");

    private static bool Run(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args) psi.ArgumentList.Add(arg);
            using Process? p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(30_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Records the last call so routing can be asserted without a real probe.</summary>
    private sealed class RecordingProbe : IVisualMediaProbe
    {
        private readonly VisualMediaInfo _result;
        public VisualMediaKind? LastKind { get; private set; }

        public RecordingProbe(VisualMediaInfo result) => _result = result;

        public Task<VisualMediaInfo> ProbeAsync(string filePath, VisualMediaKind kind, CancellationToken cancellationToken = default)
        {
            LastKind = kind;
            return Task.FromResult(_result);
        }
    }
}
