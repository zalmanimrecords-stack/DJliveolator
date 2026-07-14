using Liveolator.Core.Analysis;
using Xunit;

namespace Liveolator.Audio.Tests;

public class WavAudioDecoderTests
{
    private readonly WavAudioDecoder _decoder = new();

    private static async Task<float[]> DecodeAll(IAudioDecoder decoder, string path, int rate)
    {
        var samples = new List<float>();
        await foreach (ReadOnlyMemory<float> block in decoder.DecodeMonoAsync(path, rate))
            samples.AddRange(block.ToArray());
        return samples.ToArray();
    }

    [Theory]
    [InlineData("song.wav", true)]
    [InlineData("SONG.WAV", true)]
    [InlineData("song.mp3", false)]
    [InlineData("song.flac", false)]
    public void CanDecode_OnlyAcceptsWavByExtension(string path, bool expected)
        => Assert.Equal(expected, _decoder.CanDecode(path));

    [Fact]
    public async Task Decode_Mono16Bit_PreservesSampleCountAndValues()
    {
        float[] mono = { 0f, 0.5f, -0.5f, 1.0f, -1.0f };
        string path = WavTestFile.WritePcm16(mono, channels: 1, sampleRate: 44100);
        try
        {
            float[] result = await DecodeAll(_decoder, path, 44100);

            Assert.Equal(mono.Length, result.Length);
            for (int i = 0; i < mono.Length; i++)
                Assert.Equal(mono[i], result[i], precision: 3); // 16-bit quantization tolerance
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Decode_Stereo_DownmixesToChannelAverage()
    {
        // Two frames: L/R = (1,-1) → 0, and (0.4, 0.6) → 0.5.
        float[] interleaved = { 1.0f, -1.0f, 0.4f, 0.6f };
        string path = WavTestFile.WritePcm16(interleaved, channels: 2, sampleRate: 44100);
        try
        {
            float[] result = await DecodeAll(_decoder, path, 44100);

            Assert.Equal(2, result.Length);
            Assert.Equal(0f, result[0], precision: 3);
            Assert.Equal(0.5f, result[1], precision: 3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Decode_Float32_RoundTripsExactly()
    {
        float[] mono = { 0f, 0.25f, -0.75f, 0.999f };
        string path = WavTestFile.WriteFloat32(mono, channels: 1, sampleRate: 48000);
        try
        {
            float[] result = await DecodeAll(_decoder, path, 48000);

            Assert.Equal(mono.Length, result.Length);
            for (int i = 0; i < mono.Length; i++)
                Assert.Equal(mono[i], result[i], precision: 6); // float is lossless through the pipeline
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Decode_ResamplesToTargetRate()
    {
        // 100 source samples at 44100 → ~half as many at 22050.
        var mono = new float[100];
        for (int i = 0; i < mono.Length; i++)
            mono[i] = MathF.Sin(i * 0.1f);
        string path = WavTestFile.WritePcm16(mono, channels: 1, sampleRate: 44100);
        try
        {
            float[] result = await DecodeAll(_decoder, path, 22050);

            Assert.InRange(result.Length, 48, 52); // 100 * (22050/44100) ≈ 50
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Decode_NonRiffFile_Throws()
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-bad-{Guid.NewGuid():N}.wav");
        await File.WriteAllTextAsync(path, "this is not a wav file");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => DecodeAll(_decoder, path, 44100));
        }
        finally { File.Delete(path); }
    }
}
