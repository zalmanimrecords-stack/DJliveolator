using System;
using System.Collections.Generic;
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

    // --- Low-frequency (kick) band ---

    [Fact]
    public void Build_WithoutSampleRate_HasNoLowBand()
    {
        var overview = WaveformBuilder.Build(new float[1000], bucketCount: 32);

        Assert.False(overview.HasLowBand);
        Assert.Null(overview.LowPeaks);
    }

    [Fact]
    public void Build_WithSampleRate_PopulatesLowBand_AlignedToPeaks()
    {
        var overview = WaveformBuilder.Build(Sine(60, 8_000, 8_000, 0.8f), bucketCount: 32, sampleRate: 8_000);

        Assert.True(overview.HasLowBand);
        Assert.Equal(overview.Peaks.Count, overview.LowPeaks!.Count);
    }

    [Fact]
    public void Build_LowBand_PassesKickFrequency_AttenuatesHighFrequency()
    {
        // Equal-amplitude tones: a 60 Hz "kick" and a 3 kHz "hi-hat" at an 8 kHz overview rate.
        const int fs = 8_000;
        var lowTone = WaveformBuilder.Build(Sine(60, fs, fs, 0.8f), bucketCount: 16, sampleRate: fs);
        var highTone = WaveformBuilder.Build(Sine(3_000, fs, fs, 0.8f), bucketCount: 16, sampleRate: fs);

        // Broadband sees both tones at full amplitude.
        Assert.True(Max(lowTone.Peaks) > 0.7f);
        Assert.True(Max(highTone.Peaks) > 0.7f);

        // The kick band keeps the low tone but rejects the high tone — so kicks read clearly.
        float kickOfLow = Max(lowTone.LowPeaks!);
        float kickOfHigh = Max(highTone.LowPeaks!);
        Assert.True(kickOfLow > 0.5f, $"60 Hz should pass the kick band, got {kickOfLow}");
        Assert.True(kickOfHigh < 0.2f, $"3 kHz should be attenuated, got {kickOfHigh}");
        Assert.True(kickOfLow > kickOfHigh * 3f);
    }

    private static float[] Sine(double frequencyHz, int sampleRate, int sampleCount, float amplitude)
    {
        var samples = new float[sampleCount];
        double step = 2.0 * Math.PI * frequencyHz / sampleRate;
        for (int i = 0; i < sampleCount; i++)
            samples[i] = amplitude * (float)Math.Sin(step * i);
        return samples;
    }

    private static float Max(IReadOnlyList<float> values)
    {
        float max = 0f;
        foreach (float v in values)
            if (v > max) max = v;
        return max;
    }
}
