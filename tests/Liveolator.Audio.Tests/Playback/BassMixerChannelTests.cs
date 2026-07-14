using System;
using Liveolator.Audio.Playback;
using Liveolator.Core.Mixer;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

public class BassMixerChannelTests
{
    private static float[] Ramp(int frames, int channels)
    {
        var buffer = new float[frames * channels];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++)
                buffer[(f * channels) + c] = 0.1f * (f + 1) * (c + 1);
        return buffer;
    }

    [Fact]
    public void Default_PassesSignalThrough()
    {
        // Unity gain + all-bypass biquads must not alter the signal.
        var channel = new BassMixerChannel(channels: 2);
        float[] input = Ramp(8, 2);
        var buffer = (float[])input.Clone();

        channel.Process(buffer, channels: 2);

        for (int i = 0; i < input.Length; i++)
            Assert.Equal(input[i], buffer[i], 5);
    }

    [Fact]
    public void SetVolume_ScalesSignal()
    {
        var channel = new BassMixerChannel(channels: 2);
        channel.SetVolume(0.5);
        float[] input = Ramp(8, 2);
        var buffer = (float[])input.Clone();

        channel.Process(buffer, channels: 2);

        for (int i = 0; i < input.Length; i++)
            Assert.Equal(input[i] * 0.5f, buffer[i], 5);
    }

    [Fact]
    public void SetVolumeZero_Silences()
    {
        var channel = new BassMixerChannel(channels: 2);
        channel.SetVolume(0.0);
        var buffer = Ramp(8, 2);

        channel.Process(buffer, channels: 2);

        Assert.All(buffer, s => Assert.Equal(0f, s, 6));
        Assert.Equal(DeckLevel.Silent, channel.Level);
    }

    [Fact]
    public void Process_PublishesPostGainPeakAndRms()
    {
        var channel = new BassMixerChannel(channels: 1);
        channel.SetVolume(0.5);
        float[] buffer = [1.0f, -0.5f, 0.0f, 0.5f];

        channel.Process(buffer, channels: 1);

        Assert.Equal(0.5, channel.Level.Peak, 5);
        Assert.Equal(Math.Sqrt(0.375 / 4.0), channel.Level.Rms, 5);
    }

    [Fact]
    public void FlatEq_FromMixerMath_IsTransparent()
    {
        // Core derives a bypass biquad for a flat band; routing it must not colour the signal.
        var channel = new BassMixerChannel(channels: 1);
        channel.SetEqBand(EqBand.Low, MixerMath.EqBandCoefficients(EqBand.Low, EqBands.Flat, 48_000));
        channel.SetEqBand(EqBand.Mid, MixerMath.EqBandCoefficients(EqBand.Mid, EqBands.Flat, 48_000));
        channel.SetEqBand(EqBand.High, MixerMath.EqBandCoefficients(EqBand.High, EqBands.Flat, 48_000));

        float[] input = Ramp(16, 1);
        var buffer = (float[])input.Clone();
        channel.Process(buffer, channels: 1);

        for (int i = 0; i < input.Length; i++)
            Assert.Equal(input[i], buffer[i], 4);
    }

    [Fact]
    public void Eq_PerChannelStateIsIndependent()
    {
        // A boosted EQ must produce the same output for two identical channels — proves the
        // filter history is kept per audio channel, not shared (which would cross-contaminate).
        var channel = new BassMixerChannel(channels: 2);
        var eq = EqBands.Flat with { Low = 1.0 };
        channel.SetEqBand(EqBand.Low, MixerMath.EqBandCoefficients(EqBand.Low, eq, 48_000));

        var buffer = new float[32];
        for (int f = 0; f < 16; f++) { buffer[f * 2] = 0.5f; buffer[(f * 2) + 1] = 0.5f; }

        channel.Process(buffer, channels: 2);

        for (int f = 0; f < 16; f++)
            Assert.Equal(buffer[f * 2], buffer[(f * 2) + 1], 6);
    }

    [Fact]
    public void SetCue_TogglesFlag()
    {
        var channel = new BassMixerChannel(channels: 2);
        Assert.False(channel.CueEnabled);
        channel.SetCue(true);
        Assert.True(channel.CueEnabled);
    }

    [Fact]
    public void Process_ChannelMismatch_Throws()
    {
        var channel = new BassMixerChannel(channels: 2);
        Assert.Throws<ArgumentException>(() => channel.Process(new float[4], channels: 1));
    }
}
