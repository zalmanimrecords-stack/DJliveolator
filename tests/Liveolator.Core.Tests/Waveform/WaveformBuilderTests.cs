using System;
using Liveolator.Core.Waveform;
using Xunit;

namespace Liveolator.Core.Tests.Waveform;

/// <summary>
/// The pure peak reducer: mono samples → a fixed number of 0..1 max-abs buckets the UI can render at any
/// width. Tested off any decode/native path.
/// </summary>
public sealed class WaveformBuilderTests
{
    [Fact]
    public void Build_ProducesRequestedBucketCount()
    {
        var samples = new float[1000];
        var overview = WaveformBuilder.Build(samples, bucketCount: 64);

        Assert.Equal(64, overview.Count);
    }

    [Fact]
    public void Build_EachBucket_IsTheMaxAbsoluteAmplitudeInItsRange()
    {
        // Two halves: quiet 0.1 then loud -0.8. Two buckets must read 0.1 and 0.8.
        var samples = new float[100];
        for (int i = 0; i < 50; i++) samples[i] = 0.1f;
        for (int i = 50; i < 100; i++) samples[i] = -0.8f;

        var overview = WaveformBuilder.Build(samples, bucketCount: 2);

        Assert.Equal(0.1f, overview.Peaks[0], 3);
        Assert.Equal(0.8f, overview.Peaks[1], 3);
    }

    [Fact]
    public void Build_ClampsAmplitudesAboveUnity()
    {
        var samples = new[] { 1.5f, -2.0f, 0.5f };

        var overview = WaveformBuilder.Build(samples, bucketCount: 1);

        Assert.Equal(1.0f, overview.Peaks[0], 3);
    }

    [Fact]
    public void Build_EmptySamples_ReturnsEmptyOverview()
    {
        var overview = WaveformBuilder.Build(ReadOnlySpan<float>.Empty, bucketCount: 32);

        Assert.True(overview.IsEmpty);
        Assert.Equal(0, overview.Count);
    }

    [Fact]
    public void Build_MoreBucketsThanSamples_StillFillsEveryBucket()
    {
        var samples = new[] { 0.3f, 0.6f, 0.9f };

        var overview = WaveformBuilder.Build(samples, bucketCount: 10);

        Assert.Equal(10, overview.Count);
        Assert.All(overview.Peaks, p => Assert.InRange(p, 0f, 1f));
        // The loudest sample must surface in at least one bucket (no data dropped).
        Assert.Contains(overview.Peaks, p => Math.Abs(p - 0.9f) < 1e-3);
    }

    [Fact]
    public void Build_NonPositiveBucketCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WaveformBuilder.Build(new float[10], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WaveformBuilder.Build(new float[10], -1));
    }
}
