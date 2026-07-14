using System;
using System.IO;
using System.Threading.Tasks;
using Liveolator.Audio;
using Xunit;

namespace Liveolator.Audio.Tests;

/// <summary>
/// Manual/native smoke test for <see cref="BassAudioDecoder"/>: it only runs when an audio file is
/// supplied via the <c>LIVEOLATOR_TEST_AUDIO</c> environment variable AND the native BASS library is
/// present next to the test output — otherwise it no-ops (CI has neither). It proves the BASS decode →
/// mono → resample path actually yields samples for a real compressed file (mp3/flac/…), which is the
/// path the deck waveform uses.
/// </summary>
public sealed class BassAudioDecoderSmokeTests
{
    [Fact]
    public async Task DecodesRealFileToMonoSamples_WhenAssetAndNativeBassPresent()
    {
        string? path = Environment.GetEnvironmentVariable("LIVEOLATOR_TEST_AUDIO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return; // no asset configured → skip (the CI condition)

        var decoder = new BassAudioDecoder();
        if (!decoder.CanDecode(path))
            return; // native BASS absent (no-sound init failed) → skip rather than fail in CI

        long sampleCount = 0;
        float peak = 0f;
        await foreach (ReadOnlyMemory<float> block in decoder.DecodeMonoAsync(path, targetSampleRate: 8_000))
        {
            sampleCount += block.Length;
            peak = Math.Max(peak, MaxAbs(block));
        }

        Assert.True(sampleCount > 8_000, $"expected >1s of 8 kHz mono samples, got {sampleCount}.");
        Assert.True(peak > 0f, "decoded audio was all silence — the decode produced no signal.");
    }

    // Peak of a block, computed in a non-async helper so the ReadOnlySpan stays out of the iterator method.
    private static float MaxAbs(ReadOnlyMemory<float> block)
    {
        float max = 0f;
        ReadOnlySpan<float> span = block.Span;
        for (int i = 0; i < span.Length; i++)
        {
            float a = Math.Abs(span[i]);
            if (a > max) max = a;
        }
        return max;
    }
}
