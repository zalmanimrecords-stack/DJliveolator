using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Liveolator.Audio.Render;
using Liveolator.Core.Analysis;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Audio.Tests.Render;

public class OfflineMixRendererTests
{
    // A decoder that returns a constant DC value for any path — so the rendered output is predictable.
    private sealed class ConstantDecoder : IAudioDecoder
    {
        private readonly float _value;
        private readonly int _lengthSamples;

        public ConstantDecoder(float value, int lengthSamples)
        {
            _value = value;
            _lengthSamples = lengthSamples;
        }

        public bool CanDecode(string filePath) => true;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var block = new float[_lengthSamples];
            Array.Fill(block, _value);
            yield return block;
        }
    }

    // The render output is interleaved 16-bit stereo (L0,R0,L1,R1,...). These helpers read back each channel.
    private static (float[] Left, float[] Right) ReadWavStereo(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4));
        int frames = dataBytes / 4;   // 2 channels * 2 bytes
        var left = new float[frames];
        var right = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            left[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + (i * 4), 2)) / (float)short.MaxValue;
            right[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + (i * 4) + 2, 2)) / (float)short.MaxValue;
        }
        return (left, right);
    }

    private static short ReadChannelCount(string path)
        => BinaryPrimitives.ReadInt16LittleEndian(File.ReadAllBytes(path).AsSpan(22, 2));

    // Render and return the left channel (mono sources duplicate to both, so L == R for those projects).
    private static async Task<float[]> Render(StudioProject project, IAudioDecoder decoder, int sampleRate)
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-render-{Guid.NewGuid():N}.wav");
        try
        {
            await new OfflineMixRenderer(decoder).RenderAsync(project, path, sampleRate);
            return ReadWavStereo(path).Left;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Render_OneClip_FlatControls_ReproducesSourceLevel()
    {
        const int rate = 8_000;
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)) },
            Array.Empty<AutomationLane>());

        float[] outSamples = await Render(project, new ConstantDecoder(0.5f, rate * 2), rate);

        Assert.Equal(rate, outSamples.Length); // 1 second
        // Flat EQ + filter are bypass, gain defaults to unity → the 0.5 DC survives (mid-buffer).
        Assert.InRange(outSamples[rate / 2], 0.49f, 0.51f);
    }

    [Fact]
    public async Task Render_GainAutomation_RisesAcrossTheClip()
    {
        const int rate = 8_000;
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)) },
            new[]
            {
                new AutomationLane(AutomationTarget.DeckGain, 0, new[]
                {
                    new AutomationKeyframe(0, 0.0),
                    new AutomationKeyframe(1, 1.0),
                }),
            });

        float[] outSamples = await Render(project, new ConstantDecoder(0.8f, rate * 2), rate);

        Assert.True(outSamples[rate / 10] < outSamples[rate * 9 / 10]); // later is louder
        Assert.InRange(outSamples[0], 0f, 0.05f);                       // starts near silent
    }

    [Fact]
    public async Task Render_EmptyProject_WritesEmptyWav()
    {
        float[] outSamples = await Render(StudioProject.Empty("empty"), new ConstantDecoder(0.5f, 100), 8_000);
        Assert.Empty(outSamples);
    }

    [Fact]
    public async Task Render_TwoDecksOverlap_SumsBothSources()
    {
        const int rate = 8_000;
        var project = new StudioProject("p", 120, new[]
        {
            new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new StudioClip(2, "/m/b.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        }, Array.Empty<AutomationLane>());

        float[] outSamples = await Render(project, new ConstantDecoder(0.3f, rate * 2), rate);

        // Two decks each at 0.3 sum to ~0.6 - below the limiter ceiling, so it passes through unchanged.
        Assert.InRange(outSamples[rate / 2], 0.58f, 0.62f);
    }

    [Fact]
    public async Task Render_DecksSumAboveUnity_MasterIsLimitedBelowCeiling()
    {
        // Four decks each at 0.4 DC sum to ~1.6 - far past full scale. Without the master limiter this
        // hard-clips to +/-1.0 in the WAV (audible distortion); with it the true peak must stay at or
        // below the limiter's default -1.0 dBTP ceiling (~0.8913 linear) and never touch full scale.
        const int rate = 8_000;
        const float ceiling = 0.8913f;   // 10^(-1.0/20): MasterLimiter default true-peak ceiling
        var project = new StudioProject("p", 120, new[]
        {
            new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new StudioClip(1, "/m/b.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new StudioClip(2, "/m/c.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new StudioClip(3, "/m/d.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        }, Array.Empty<AutomationLane>());

        float[] outSamples = await Render(project, new ConstantDecoder(0.4f, rate * 2), rate);

        Assert.NotEmpty(outSamples);
        float peak = 0f;
        foreach (float s in outSamples)
            peak = Math.Max(peak, Math.Abs(s));

        // Output peak must be held at/under the ceiling (small tolerance for 16-bit quantisation), and
        // no sample may sit at full-scale clip.
        Assert.True(peak <= ceiling + 0.01f, $"peak {peak} exceeded ceiling {ceiling}");
        Assert.True(peak < 0.999f, $"peak {peak} indicates hard clipping at full scale");

        // After the limiter settles (mid-buffer, well past attack/look-ahead), the steady DC must be
        // pulled down close to the ceiling rather than passed through at ~1.6 or clipped at 1.0.
        Assert.InRange(outSamples[rate / 2], 0.80f, ceiling + 0.01f);
    }

    [Fact]
    public async Task Render_WritesTwoChannelWavHeader()
    {
        const int rate = 8_000;
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)) },
            Array.Empty<AutomationLane>());

        string path = Path.Combine(Path.GetTempPath(), $"liveolator-render-{Guid.NewGuid():N}.wav");
        try
        {
            await new OfflineMixRenderer(new ConstantDecoder(0.5f, rate * 2)).RenderAsync(project, path, rate);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22, 2)));    // channels
            Assert.Equal(rate, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4))); // sample rate
            Assert.Equal(rate * 2 * 2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28, 4))); // byte rate
            Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(32, 2)));    // block align (2ch * 2B)
            Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34, 2)));   // bits

            // One second of stereo frames = rate frames * 2 channels * 2 bytes.
            int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4));
            Assert.Equal(rate * 2 * 2, dataBytes);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Render_DistinctLeftRightSource_RoundTripsThePanDifference()
    {
        // A genuinely stereo source (L louder than R) must survive the stereo render: the rendered WAV's
        // left channel must read meaningfully hotter than its right. This exercises the independent
        // per-channel path (decode -> per-channel biquad -> interleaved master -> stereo WAV). The decode
        // override injects distinct L/R DC without needing real BASS.
        const int rate = 8_000;
        const float leftDc = 0.6f;
        const float rightDc = 0.2f;
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)) },
            Array.Empty<AutomationLane>());

        StereoBuffer StereoDc(string _, double __)
        {
            var l = new float[rate * 2];
            var r = new float[rate * 2];
            Array.Fill(l, leftDc);
            Array.Fill(r, rightDc);
            return new StereoBuffer(l, r);
        }

        string path = Path.Combine(Path.GetTempPath(), $"liveolator-render-{Guid.NewGuid():N}.wav");
        try
        {
            var renderer = new OfflineMixRenderer(new ConstantDecoder(0f, rate * 2), logger: null, decodeOverride: StereoDc);
            await renderer.RenderAsync(project, path, rate);

            Assert.Equal(2, ReadChannelCount(path));
            (float[] left, float[] right) = ReadWavStereo(path);

            // Flat controls, unity gain → each channel reproduces its own DC (mid-buffer, past any settle).
            Assert.InRange(left[rate / 2], leftDc - 0.02f, leftDc + 0.02f);
            Assert.InRange(right[rate / 2], rightDc - 0.02f, rightDc + 0.02f);
            Assert.True(left[rate / 2] > right[rate / 2] + 0.2f, "left channel must stay hotter than right");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
