using Liveolator.Audio;
using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Integration.Tests;

public class WavAudioDecoderTests
{
    private const int Sr = 44100;

    private static async Task<float[]> DecodeAll(string path, int targetSr)
    {
        var pcm = new List<float>();
        await foreach (var block in new WavAudioDecoder().DecodeMonoAsync(path, targetSr))
            pcm.AddRange(block.ToArray());
        return pcm.ToArray();
    }

    [Theory]
    [InlineData("song.wav", true)]
    [InlineData("song.WAV", true)]
    [InlineData("song.mp3", false)]
    public void CanDecode_ByExtension(string path, bool expected)
        => Assert.Equal(expected, new WavAudioDecoder().CanDecode(path));

    [Fact]
    public async Task Decode_Mono16Bit_RecoversBpm()
    {
        using var dir = new TempDir();
        string path = dir.Write("beat.wav", TestMedia.Pcm16Wav(TestMedia.ClickTrain(120, Sr, 8), Sr));

        float[] mono = await DecodeAll(path, Sr);

        Assert.InRange(mono.Length, (int)(Sr * 8 * 0.99), (int)(Sr * 8 * 1.01));
        BpmResult bpm = new BpmDetector().Detect(mono, Sr);
        Assert.InRange(bpm.Bpm, 117.0, 123.0);
    }

    [Fact]
    public async Task Decode_Stereo_DownmixesToMonoFrames()
    {
        using var dir = new TempDir();
        var left = TestMedia.ClickTrain(120, Sr, 2);
        var right = new float[left.Length];
        string path = dir.Write("st.wav", TestMedia.Pcm16Wav(new[] { left, right }, Sr));

        float[] mono = await DecodeAll(path, Sr);

        Assert.Equal(left.Length, mono.Length); // one mono sample per stereo frame
    }

    [Fact]
    public async Task Decode_Resamples_To_TargetRate()
    {
        using var dir = new TempDir();
        string path = dir.Write("hi.wav", TestMedia.Pcm16Wav(TestMedia.ClickTrain(120, 48000, 2), 48000));

        float[] mono = await DecodeAll(path, 44100);

        int expected = (int)(48000 * 2 * (44100.0 / 48000.0));
        Assert.InRange(mono.Length, (int)(expected * 0.99), (int)(expected * 1.01));
    }

    [Fact]
    public async Task Decode_NonWavBytes_Throws()
    {
        using var dir = new TempDir();
        string path = dir.Write("fake.wav", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 });

        await Assert.ThrowsAsync<InvalidDataException>(() => DecodeAll(path, Sr));
    }
}
