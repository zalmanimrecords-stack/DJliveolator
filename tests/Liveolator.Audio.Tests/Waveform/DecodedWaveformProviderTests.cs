using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Audio.Waveform;
using Liveolator.Core.Analysis;
using Liveolator.Core.Waveform;
using Xunit;

namespace Liveolator.Audio.Tests.Waveform;

/// <summary>
/// Exercises the decode→reduce provider with a fake decoder, so the accumulation + reduction + the
/// degrade-on-failure contract are tested without any native FFmpeg/BASS.
/// </summary>
public sealed class DecodedWaveformProviderTests
{
    private sealed class FakeDecoder : IAudioDecoder
    {
        private readonly float[][] _blocks;
        private readonly bool _canDecode;
        private readonly Exception? _throw;

        public FakeDecoder(float[][] blocks, bool canDecode = true, Exception? throwOnDecode = null)
        {
            _blocks = blocks;
            _canDecode = canDecode;
            _throw = throwOnDecode;
        }

        public int RequestedSampleRate { get; private set; }

        public bool CanDecode(string filePath) => _canDecode;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RequestedSampleRate = targetSampleRate;
            if (_throw is not null)
                throw _throw;
            foreach (float[] block in _blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return block;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task GetOverview_AccumulatesBlocksAndReducesToBuckets()
    {
        // Two blocks: quiet then loud. Two buckets must read the per-half peak.
        var decoder = new FakeDecoder(new[]
        {
            new[] { 0.2f, 0.2f, 0.2f, 0.2f },
            new[] { -0.9f, 0.9f, -0.9f, 0.9f },
        });
        var provider = new DecodedWaveformProvider(decoder);

        WaveformOverview overview = await provider.GetOverviewAsync("track.flac", bucketCount: 2);

        Assert.Equal(2, overview.Count);
        Assert.Equal(0.2f, overview.Peaks[0], 3);
        Assert.Equal(0.9f, overview.Peaks[1], 3);
    }

    [Fact]
    public async Task GetOverview_DecodesAtTheOverviewSampleRate()
    {
        var decoder = new FakeDecoder(new[] { new[] { 0.5f } });
        var provider = new DecodedWaveformProvider(decoder, overviewSampleRate: 6_000);

        await provider.GetOverviewAsync("track.flac", bucketCount: 1);

        Assert.Equal(6_000, decoder.RequestedSampleRate);
    }

    [Fact]
    public async Task GetOverview_UndecodableFile_ReturnsEmpty()
    {
        var provider = new DecodedWaveformProvider(new FakeDecoder(Array.Empty<float[]>(), canDecode: false));

        WaveformOverview overview = await provider.GetOverviewAsync("track.xyz", bucketCount: 16);

        Assert.True(overview.IsEmpty);
    }

    [Fact]
    public async Task GetOverview_DecodeThrows_ReturnsEmpty_NotThrow()
    {
        var provider = new DecodedWaveformProvider(
            new FakeDecoder(Array.Empty<float[]>(), throwOnDecode: new InvalidOperationException("boom")));

        WaveformOverview overview = await provider.GetOverviewAsync("track.flac", bucketCount: 16);

        Assert.True(overview.IsEmpty);
    }

    [Fact]
    public async Task GetOverview_Cancellation_Propagates()
    {
        var decoder = new FakeDecoder(new[] { new[] { 0.1f }, new[] { 0.2f } });
        var provider = new DecodedWaveformProvider(decoder);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetOverviewAsync("track.flac", bucketCount: 4, cts.Token));
    }

    [Fact]
    public async Task GetOverview_InvalidArguments_Throw()
    {
        var provider = new DecodedWaveformProvider(new FakeDecoder(Array.Empty<float[]>()));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetOverviewAsync("  ", 16));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.GetOverviewAsync("track.flac", 0));
    }
}
