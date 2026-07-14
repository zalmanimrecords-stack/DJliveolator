using Liveolator.Core.Audio.Effects;

namespace Liveolator.Core.Tests.Audio.Effects;

/// <summary>
/// Behavioural guards for the three in-house DSP effects. They assert the two properties the channel-strip
/// FX mode relies on — an exact dry passthrough at the neutral parameter values (so EQ mode is transparent)
/// and an audible, finite effect once a knob is turned — without depending on exact coefficient values.
/// </summary>
public sealed class BuiltInAudioEffectProcessorsTests
{
    private const int SampleRate = 48_000;

    private static float[] Tone(int frames, int channels, double freqHz)
    {
        var buffer = new float[frames * channels];
        for (int f = 0; f < frames; f++)
        {
            float s = (float)(0.5 * Math.Sin(2.0 * Math.PI * freqHz * f / SampleRate));
            for (int c = 0; c < channels; c++)
                buffer[f * channels + c] = s;
        }
        return buffer;
    }

    private static double Rms(ReadOnlySpan<float> buffer)
    {
        double sum = 0;
        foreach (float s in buffer)
            sum += (double)s * s;
        return Math.Sqrt(sum / buffer.Length);
    }

    private static bool AllFinite(ReadOnlySpan<float> buffer)
    {
        foreach (float s in buffer)
            if (float.IsNaN(s) || float.IsInfinity(s))
                return false;
        return true;
    }

    [Fact]
    public void Moog_FullyOpenNoResonance_PassesThroughUnchanged()
    {
        var moog = new MoogLadderFilterProcessor(SampleRate);
        // Defaults are the neutral values (cutoff = 1, resonance = 0).
        float[] input = Tone(256, 2, 1000);
        float[] buffer = (float[])input.Clone();

        moog.Process(buffer, channels: 2);

        Assert.Equal(input, buffer);
    }

    [Fact]
    public void Moog_LowCutoff_AttenuatesHighsMoreThanLows()
    {
        double PassRatio(double toneHz)
        {
            var moog = new MoogLadderFilterProcessor(SampleRate);
            moog.SetParameter(BuiltInAudioEffects.Cutoff, 0.25); // well below the tones below
            float[] buffer = Tone(4096, 1, toneHz);
            double inRms = Rms(buffer);
            moog.Process(buffer, channels: 1);
            return Rms(buffer) / inRms;
        }

        double lowKept = PassRatio(100);
        double highKept = PassRatio(8000);

        Assert.True(AllFinite(Tone(1, 1, 1))); // sanity of helpers
        Assert.True(highKept < lowKept, $"expected highs cut more than lows (low={lowKept:F3}, high={highKept:F3})");
        Assert.True(highKept < 0.5, $"expected a clear high-frequency cut, got {highKept:F3}");
    }

    [Fact]
    public void Moog_HighResonanceSweep_StaysFinite()
    {
        var moog = new MoogLadderFilterProcessor(SampleRate);
        moog.SetParameter(BuiltInAudioEffects.Cutoff, 0.3);
        moog.SetParameter(BuiltInAudioEffects.Resonance, 1.0); // clamped below self-oscillation internally

        for (int block = 0; block < 40; block++)
        {
            float[] buffer = Tone(512, 2, 300);
            moog.Process(buffer, channels: 2);
            Assert.True(AllFinite(buffer), $"block {block} produced non-finite output");
            Assert.True(Rms(buffer) < 8.0, $"block {block} runaway: rms={Rms(buffer):F3}");
        }
    }

    [Fact]
    public void Reverb_Dry_PassesThroughUnchanged()
    {
        var reverb = new FreeverbProcessor(SampleRate);
        float[] input = Tone(256, 2, 440);
        float[] buffer = (float[])input.Clone();

        reverb.Process(buffer, channels: 2);

        Assert.Equal(input, buffer);
    }

    [Fact]
    public void Reverb_Wet_ProducesADecayingTailAfterInput()
    {
        var reverb = new FreeverbProcessor(SampleRate);
        reverb.SetParameter(BuiltInAudioEffects.Wet, 1.0);

        // One block of tone to excite the tanks...
        float[] excite = Tone(2048, 1, 440);
        reverb.Process(excite, channels: 1);
        // ...then a block of pure silence: any output is the reverb tail.
        var silence = new float[2048];
        reverb.Process(silence, channels: 1);

        Assert.True(AllFinite(silence));
        Assert.True(Rms(silence) > 1e-4, $"expected an audible tail into silence, got rms={Rms(silence):E3}");
    }

    [Fact]
    public void Phaser_Dry_PassesThroughUnchanged()
    {
        var phaser = new PhaserProcessor(SampleRate);
        float[] input = Tone(256, 2, 440);
        float[] buffer = (float[])input.Clone();

        phaser.Process(buffer, channels: 2);

        Assert.Equal(input, buffer);
    }

    [Fact]
    public void Phaser_Wet_ChangesTheSignalAndStaysFinite()
    {
        var phaser = new PhaserProcessor(SampleRate);
        phaser.SetParameter(BuiltInAudioEffects.Wet, 1.0);
        float[] input = Tone(2048, 2, 440);
        float[] buffer = (float[])input.Clone();

        phaser.Process(buffer, channels: 2);

        Assert.True(AllFinite(buffer));
        Assert.NotEqual(input, buffer);
    }
}
