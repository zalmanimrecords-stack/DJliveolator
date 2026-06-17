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

    private static float[] ReadWavMono(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4));
        int count = dataBytes / 2;
        var samples = new float[count];
        for (int i = 0; i < count; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + (i * 2), 2)) / (float)short.MaxValue;
        return samples;
    }

    private static async Task<float[]> Render(StudioProject project, IAudioDecoder decoder, int sampleRate)
    {
        string path = Path.Combine(Path.GetTempPath(), $"liveolator-render-{Guid.NewGuid():N}.wav");
        try
        {
            await new OfflineMixRenderer(decoder).RenderAsync(project, path, sampleRate);
            return ReadWavMono(path);
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
}
