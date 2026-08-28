using System.Runtime.CompilerServices;
using Liveolator.Audio.Playback;
using Liveolator.Audio.Render;
using Liveolator.Core.Analysis;
using Liveolator.Core.Studio;

namespace Liveolator.Audio.Tests.Render;

/// <summary>
/// The render must say which clips came out in MONO. The fallback to the managed decoder is mono by seam
/// contract and fires for any clip at warp factor 1.0 whose native decode fails, with nothing but a log
/// warning — which is how eleven minutes of a measured 68-minute export shipped without a stereo image.
/// </summary>
public sealed class OfflineMixRendererMonoFallbackTests
{
    [Fact]
    public async Task Render_ReportsTheClipThatFellBackToTheMonoDecoder()
    {
        // No BASS decode is possible for this path, and the clip needs no stretch, so the render takes the
        // managed mono path — the exact configuration that produced the measured defect.
        const int rate = 8_000;
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(2)) },
            Array.Empty<AutomationLane>());

        MixRenderResult result = await RenderResult(project, new ConstantDecoder(0.5f, rate * 2), rate);

        Assert.Equal(new[] { "/m/a.wav" }, result.MonoFallbackSources);
        Assert.Empty(result.SilentSources);
    }

    [Fact]
    public async Task Render_ReportsNoMonoFallback_WhenTheDecodeSuppliesStereo()
    {
        // The other side of the gate: a source that really decoded in stereo must not be reported, or the
        // export would refuse every mix.
        const int rate = 8_000;
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(2)) },
            Array.Empty<AutomationLane>());

        StereoBuffer Stereo(string _, double __)
        {
            var left = new float[rate * 2];
            var right = new float[rate * 2];
            Array.Fill(left, 0.5f);
            Array.Fill(right, 0.25f);
            return new StereoBuffer(left, right);
        }

        string path = Path.Combine(Path.GetTempPath(), $"liveolator-mono-{Guid.NewGuid():N}.wav");
        try
        {
            var renderer = new OfflineMixRenderer(
                new ConstantDecoder(0.5f, rate * 2), logger: null, decodeOverride: Stereo);
            MixRenderResult result = await renderer.RenderAsync(project, path, rate);

            Assert.Empty(result.MonoFallbackSources);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Render_ReportsASilentSourceAsSilent_NotAsMono()
    {
        // A fallback that produced nothing is a clip absent from the mix, not a clip without a stereo image.
        // Calling it mono would send the owner after the channel count instead of after the decode.
        const int rate = 8_000;
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(2)) },
            Array.Empty<AutomationLane>());

        MixRenderResult result = await RenderResult(project, new EmptyDecoder(), rate);

        Assert.Empty(result.MonoFallbackSources);
        Assert.Equal(new[] { "/m/a.wav" }, result.SilentSources);
    }

    private static async Task<MixRenderResult> RenderResult(
        StudioProject project, IAudioDecoder decoder, int sampleRate)
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-mono-{Guid.NewGuid():N}.wav");
        try
        {
            return await new OfflineMixRenderer(decoder).RenderAsync(project, path, sampleRate);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class ConstantDecoder : IAudioDecoder
    {
        private const int BlockSize = 4096;

        private readonly float _level;
        private readonly int _samples;

        internal ConstantDecoder(float level, int samples)
        {
            _level = level;
            _samples = samples;
        }

        public bool CanDecode(string filePath) => true;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            var block = new float[BlockSize];
            Array.Fill(block, _level);

            int remaining = _samples;
            while (remaining > 0)
            {
                int take = Math.Min(BlockSize, remaining);
                yield return block.AsMemory(0, take);
                remaining -= take;
            }
        }
    }

    private sealed class EmptyDecoder : IAudioDecoder
    {
        public bool CanDecode(string filePath) => true;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
