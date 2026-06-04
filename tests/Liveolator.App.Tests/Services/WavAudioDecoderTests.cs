using System.IO;
using Liveolator.App.Services;

namespace Liveolator.App.Tests.Services;

public sealed class WavAudioDecoderTests
{
    [Fact]
    public void CanDecode_only_accepts_wav()
    {
        var decoder = new WavAudioDecoder();
        Assert.True(decoder.CanDecode("track.wav"));
        Assert.True(decoder.CanDecode("TRACK.WAV"));
        Assert.False(decoder.CanDecode("track.mp3"));
        Assert.False(decoder.CanDecode(""));
    }

    [Fact]
    public async Task Decodes_16bit_mono_pcm_to_normalized_floats()
    {
        short[] samples = { 0, 16384, -16384, 32767 };
        string path = WriteWav(samples, channels: 1, sampleRate: 8000);
        try
        {
            float[] mono = await DecodeAll(path, targetRate: 8000);

            Assert.Equal(4, mono.Length);
            Assert.Equal(0f, mono[0], 0.001);
            Assert.Equal(0.5f, mono[1], 0.001);
            Assert.Equal(-0.5f, mono[2], 0.001);
            Assert.Equal(1f, mono[3], 0.001);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Downmixes_stereo_to_mono_average()
    {
        // frame 0: L=16384 R=0 -> 0.25 ; frame 1: L=0 R=32767 -> ~0.5
        short[] interleaved = { 16384, 0, 0, 32767 };
        string path = WriteWav(interleaved, channels: 2, sampleRate: 8000);
        try
        {
            float[] mono = await DecodeAll(path, targetRate: 8000);

            Assert.Equal(2, mono.Length);
            Assert.Equal(0.25f, mono[0], 0.001);
            Assert.Equal(0.5f, mono[1], 0.01);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Resamples_to_target_rate()
    {
        var samples = new short[100];
        string path = WriteWav(samples, channels: 1, sampleRate: 8000);
        try
        {
            float[] upsampled = await DecodeAll(path, targetRate: 16000);
            Assert.InRange(upsampled.Length, 195, 205); // ~2x
        }
        finally { File.Delete(path); }
    }

    private static async Task<float[]> DecodeAll(string path, int targetRate)
    {
        var decoder = new WavAudioDecoder();
        var all = new List<float>();
        await foreach (ReadOnlyMemory<float> block in decoder.DecodeMonoAsync(path, targetRate, CancellationToken.None))
            all.AddRange(block.ToArray());
        return all.ToArray();
    }

    private static string WriteWav(short[] samples, int channels, int sampleRate)
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator_test_{Guid.NewGuid():N}.wav");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);

        int dataLen = samples.Length * sizeof(short);
        int byteRate = sampleRate * channels * sizeof(short);

        w.Write("RIFF".ToCharArray());
        w.Write(36 + dataLen);
        w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray());
        w.Write(16);
        w.Write((short)1);                       // PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)(channels * sizeof(short))); // block align
        w.Write((short)16);                      // bits per sample
        w.Write("data".ToCharArray());
        w.Write(dataLen);
        foreach (short s in samples)
            w.Write(s);

        return path;
    }
}
