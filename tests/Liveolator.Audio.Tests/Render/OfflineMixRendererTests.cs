using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Liveolator.Audio.Playback;
using Liveolator.Audio.Render;
using Liveolator.Core.Analysis;
using Liveolator.Core.Mixer;
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

    // A decoder that yields in small blocks up to a long total, recording how far enumeration actually got.
    // Lets a test assert the renderer stops decoding once it has the samples a clip needs.
    private sealed class CountingDecoder : IAudioDecoder
    {
        private readonly int _blockSize;
        private readonly int _totalSamples;

        public CountingDecoder(int blockSize, int totalSamples)
        {
            _blockSize = blockSize;
            _totalSamples = totalSamples;
        }

        public int YieldedSamples { get; private set; }

        public bool CanDecode(string filePath) => true;

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMonoAsync(
            string filePath, int targetSampleRate, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            int produced = 0;
            while (produced < _totalSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int n = Math.Min(_blockSize, _totalSamples - produced);
                var block = new float[n];
                Array.Fill(block, 0.3f);
                produced += n;
                YieldedSamples = produced;
                yield return block;
            }
        }
    }

    [Fact]
    public async Task Render_TrimmedClipOnLongTrack_DecodesOnlyWhatTheClipUses()
    {
        // A 1-second clip cut from a 30-second "file": the renderer must stop decoding shortly past the
        // clip span instead of materialising the whole track (the memory fix), while still decoding enough
        // to fill the clip.
        const int rate = 8_000;
        var decoder = new CountingDecoder(blockSize: 1_000, totalSamples: rate * 30);
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(1)) },
            Array.Empty<AutomationLane>());

        await Render(project, decoder, rate);

        Assert.True(decoder.YieldedSamples >= rate, $"must decode at least the clip span; got {decoder.YieldedSamples}");
        Assert.True(decoder.YieldedSamples < rate * 30, $"must stop before the whole file; got {decoder.YieldedSamples}");
    }

    [Fact]
    public async Task Render_OpenEndedClip_DecodesTheWholeFile()
    {
        // No out-point ⇒ the clip may need the whole file, so the decode is not capped.
        const int rate = 8_000;
        int total = rate * 5;
        var decoder = new CountingDecoder(blockSize: 1_000, totalSamples: total);
        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, SourceOut: null) },
            Array.Empty<AutomationLane>());

        await Render(project, decoder, rate);

        Assert.Equal(total, decoder.YieldedSamples);
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

    // The renderer walks the output in fixed BlockSize chunks; this must match the renderer's constant.
    private const int RenderBlockSize = 256;
    private const double FilterLowPassKnob = 0.2;  // below 0.5 = a low-pass that rings on a step
    private const double FilterHighPassKnob = 0.8; // above 0.5 = a high-pass: blocks DC, so a step rings then decays to ~0

    // A reference single, continuous biquad cascade pass over a whole mono buffer using the same MixerMath
    // coefficients + StatefulBiquad the renderer uses - i.e. what the rendered output MUST equal if delay
    // state truly persists across every block boundary (no per-block reset).
    private static float[] ContinuousFilterPass(float[] mono, double filterKnob, int sampleRate)
        => ContinuousFilterPass(mono, filterKnob, sampleRate, primeWith: null);

    // Run the renderer's exact 4-stage cascade (flat EQ + a filter) over a mono buffer from zero history.
    // When primeWith is supplied, the cascade is first warmed up over that buffer (without keeping its
    // output) so the delay state at the start of mono mimics a carried-over (un-reset) source - the
    // discontinuity a source-boundary reset must AVOID.
    private static float[] ContinuousFilterPass(float[] mono, double filterKnob, int sampleRate, float[]? primeWith)
    {
        var low = new StatefulBiquad(1);
        var mid = new StatefulBiquad(1);
        var high = new StatefulBiquad(1);
        var filt = new StatefulBiquad(1);
        low.SetCoefficients(MixerMath.EqBandCoefficients(EqBand.Low, EqBands.Flat, sampleRate));
        mid.SetCoefficients(MixerMath.EqBandCoefficients(EqBand.Mid, EqBands.Flat, sampleRate));
        high.SetCoefficients(MixerMath.EqBandCoefficients(EqBand.High, EqBands.Flat, sampleRate));
        filt.SetCoefficients(MixerMath.FilterCoefficients(filterKnob, sampleRate));

        if (primeWith is not null)
            foreach (float p in primeWith)
                filt.Process(0, high.Process(0, mid.Process(0, low.Process(0, p))));

        var outBuf = new float[mono.Length];
        for (int i = 0; i < mono.Length; i++)
            outBuf[i] = (float)filt.Process(0, high.Process(0, mid.Process(0, low.Process(0, mono[i]))));
        return outBuf;
    }

    [Fact]
    public async Task Render_NonFlatFilter_IsContinuousAcrossBlockBoundary()
    {
        // The deck biquads must carry delay state continuously over the whole stream, exactly as the live
        // mixer does. So a single steady clip through a non-flat (low-pass) filter must render IDENTICALLY
        // to one continuous-state biquad pass over the same buffer. If the renderer reset/recreated the
        // delay state at each ~256-sample block boundary, the low-pass step transient would reappear at
        // every boundary and the rendered output would diverge from this single-pass reference there.
        const int rate = 8_000;
        const float dc = 0.5f;
        int lengthSamples = RenderBlockSize * 6; // several block boundaries inside one contiguous clip

        var project = new StudioProject("p", 120,
            new[] { new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(lengthSamples / (double)rate)) },
            new[]
            {
                new AutomationLane(AutomationTarget.Filter, 0, new[] { new AutomationKeyframe(0, FilterLowPassKnob) }),
            });

        float[] outSamples = await Render(project, new ConstantDecoder(dc, lengthSamples), rate);

        var source = new float[lengthSamples];
        Array.Fill(source, dc);
        float[] expected = ContinuousFilterPass(source, FilterLowPassKnob, rate);

        Assert.Equal(lengthSamples, outSamples.Length);
        // Whole-stream equality within 16-bit quantisation: identical to one continuous-state pass.
        for (int i = 0; i < lengthSamples; i++)
            Assert.True(Math.Abs(outSamples[i] - expected[i]) < 3e-4f,
                $"sample {i}: rendered {outSamples[i]} vs continuous {expected[i]}");

        // Pin the boundary itself: rendered and continuous step the same way across the first block edge,
        // so there is no per-block-reset jump there.
        int b = RenderBlockSize;
        Assert.True(Math.Abs(outSamples[b] - expected[b]) < 3e-4f, "block boundary diverged from continuous pass");
    }

    [Fact]
    public async Task Render_NewClipAfterGap_DoesNotInheritPreviousFilterRing()
    {
        // A genuine source discontinuity (the deck goes silent, then a different clip starts on the SAME
        // deck) must reset the biquad delay state - mirroring a freshly loaded live stream. Otherwise the
        // new clip inherits the previous clip's filter state (a click the live preview never has).
        //
        // A high-pass filter discriminates reset vs carry: it blocks DC, so during clip A the cascade
        // settles its delay state toward a steady DC-rejecting condition. A FRESH stream at clip B's onset
        // sees a step (0 -> dc) and produces a decaying spike; a CARRIED state continues near zero with no
        // spike. The rendered second clip must match the fresh pass and clearly differ from the carried one.
        const int rate = 8_000;
        const float dc = 0.5f;
        int clipSamples = RenderBlockSize * 4;
        double clipSecs = clipSamples / (double)rate;
        double gapSecs = clipSecs;          // a full clip-length of silence between the two clips
        double secondStart = clipSecs + gapSecs;

        var project = new StudioProject("p", 120,
            new[]
            {
                // Both clips on deck slot 0; bounded length so the deck genuinely goes silent in the gap.
                new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(clipSecs)),
                new StudioClip(0, "/m/b.wav", secondStart, TimeSpan.Zero, TimeSpan.FromSeconds(clipSecs)),
            },
            new[]
            {
                new AutomationLane(AutomationTarget.Filter, 0, new[] { new AutomationKeyframe(0, FilterHighPassKnob) }),
            });

        float[] outSamples = await Render(project, new ConstantDecoder(dc, clipSamples), rate);

        var source = new float[clipSamples];
        Array.Fill(source, dc);
        // Fresh (reset) reference: clip B filtered from ZERO history - what a freshly loaded live stream gives.
        float[] freshSecond = ContinuousFilterPass(source, FilterHighPassKnob, rate);
        // Carried (un-reset) reference: clip B filtered through state primed by clip A's audio - the bug.
        float[] carriedSecond = ContinuousFilterPass(source, FilterHighPassKnob, rate, primeWith: source);

        // The two references must genuinely diverge at the onset, else this test cannot discriminate.
        Assert.True(Math.Abs(freshSecond[0] - carriedSecond[0]) > 0.05f,
            "test signal must distinguish fresh vs carried filter state at the onset");

        int secondStartSample = (int)Math.Round(secondStart * rate);
        for (int i = 0; i < clipSamples; i++)
        {
            int idx = secondStartSample + i;
            if (idx >= outSamples.Length) break;
            Assert.True(Math.Abs(outSamples[idx] - freshSecond[i]) < 3e-4f,
                $"second clip sample {i}: rendered {outSamples[idx]} must match fresh-stream {freshSecond[i]}");
        }

        // And explicitly NOT the carried-state continuation at the onset.
        Assert.True(Math.Abs(outSamples[secondStartSample] - carriedSecond[0]) > 0.05f,
            "second clip onset must not inherit the previous clip's filter state");
    }
}
