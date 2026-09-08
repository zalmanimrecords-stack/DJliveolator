using Liveolator.Audio.Render;

namespace Liveolator.Audio.Tests.Render;

/// <summary>
/// A ramped decode must come out CONTINUOUS. The tempo is moved while the stretcher is running, and a
/// stretcher that restarts its window on every change stitches the output back together at a
/// discontinuity — which is a click, and a click every pull block is the sound of a ruined mix.
/// <para>Measured on a pure tone, where the largest step a clean signal can take between samples is known
/// exactly (2*pi*f*A/rate); anything far above that is a seam, not music.</para>
/// </summary>
public sealed class RampedDecodeContinuityTests : IDisposable
{
    private const int SampleRate = 44_100;
    private const double ToneHz = 200.0;
    private const double ToneAmplitude = 0.5;
    private const double LengthSeconds = 12.0;

    /// <summary>The largest sample-to-sample step the tone itself can take, plus room for the stretcher's
    /// own resampling. A seam overshoots this many times over, so the exact headroom is not delicate.</summary>
    private static double CleanStepCeiling =>
        2.0 * Math.PI * ToneHz * ToneAmplitude / SampleRate * 8.0;

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"liveolator-ramp-continuity-{Guid.NewGuid():N}.wav");

    [Fact]
    public void ARampedDecode_IsAsContinuousAsAConstantOne()
    {
        WriteTone(_path);
        var decoder = new BassFxRenderDecoder();

        // Same stretch either side of the comparison; the ONLY difference is whether it moves.
        StereoBuffer steady = decoder.DecodeRampedStereo(_path, SampleRate, 0.0, _ => 2.0);
        StereoBuffer ramped = decoder.DecodeRampedStereo(
            _path, SampleRate, 0.0, seconds => 1.0 + (seconds / LengthSeconds));

        Assert.True(steady.Length > SampleRate, "no audio — are the BASS natives present?");
        Assert.True(ramped.Length > SampleRate, "no ramped audio");

        double steadyWorst = WorstStep(steady);
        double rampedWorst = WorstStep(ramped);

        Assert.True(
            steadyWorst <= CleanStepCeiling,
            $"even a steady stretch is discontinuous: worst step {steadyWorst:F4} > {CleanStepCeiling:F4}");
        Assert.True(
            rampedWorst <= CleanStepCeiling,
            $"moving the tempo tore the output: worst step {rampedWorst:F4} > {CleanStepCeiling:F4} " +
            $"(a steady stretch of the same material reaches only {steadyWorst:F4})");
    }

    [Fact]
    public void MovingTheTempo_DoesNotSwallowSamples()
    {
        // The failure a continuity check cannot see: a stretcher that flushes its buffer on every tempo
        // change loses whatever was in it, so the output is SHORT by a little on every change. Eleven
        // changes a second and the mix stutters its way through the record while every individual sample
        // boundary still looks clean. Length is the honest witness — it cannot hide dropped audio.
        WriteTone(_path);
        var decoder = new BassFxRenderDecoder();

        // Both average 1.5% over the clip, so a stretcher that keeps everything returns the same length.
        StereoBuffer steady = decoder.DecodeRampedStereo(_path, SampleRate, 0.0, _ => 1.5);
        StereoBuffer ramped = decoder.DecodeRampedStereo(
            _path, SampleRate, 0.0, seconds => 1.0 + Math.Min(1.0, seconds / LengthSeconds));

        Assert.True(steady.Length > SampleRate, "no audio — are the BASS natives present?");
        double steadySeconds = steady.Length / (double)SampleRate;
        double rampedSeconds = ramped.Length / (double)SampleRate;

        Assert.True(
            Math.Abs(rampedSeconds - steadySeconds) < 0.05,
            $"a moving tempo produced {rampedSeconds:F3}s where the same average held steady produced " +
            $"{steadySeconds:F3}s — {(steadySeconds - rampedSeconds) * 1000.0:F0} ms of audio went missing");
    }

    // The largest jump between consecutive samples, ignoring the very start where the stretcher is priming.
    private static double WorstStep(StereoBuffer buffer)
    {
        int from = SampleRate / 4;
        double worst = 0.0;
        for (int i = from + 1; i < buffer.Length; i++)
            worst = Math.Max(worst, Math.Abs(buffer.Left[i] - buffer.Left[i - 1]));
        return worst;
    }

    private static void WriteTone(string path)
    {
        int frames = (int)(LengthSeconds * SampleRate);
        var samples = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            double value = ToneAmplitude * Math.Sin(2.0 * Math.PI * ToneHz * i / SampleRate);
            samples[i] = (short)(value * short.MaxValue);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        int dataBytes = samples.Length * sizeof(short);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        foreach (short sample in samples)
            writer.Write(sample);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
