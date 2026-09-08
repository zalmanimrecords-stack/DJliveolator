using System.Text;
using Liveolator.Audio.Render;
using Xunit.Abstractions;

namespace Liveolator.Audio.Tests.Render;

/// <summary>
/// Measures where the time-stretcher actually puts a known transient, against where the render geometry
/// assumes it goes (source second s at output second s/factor). Any difference is a fixed offset the
/// offline mix inherits on every warped clip — which is the shape of "the kicks nearly sit".
/// <para>It prints the measurement AND asserts on it, so the fixed-latency correction that closed this gap
/// cannot be removed without a red test. Needs the BASS natives present to say anything.</para>
/// </summary>
public sealed class StretchLatencyProbe : IDisposable
{
    private const int SampleRate = 44_100;
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"liveolator-stretch-probe-{Guid.NewGuid():N}.wav");
    private readonly ITestOutputHelper _out;

    private readonly List<string> _report = new();

    public StretchLatencyProbe(ITestOutputHelper output) => _out = output;

    private void Line(string text)
    {
        _out.WriteLine(text);
        _report.Add(text);
    }

    [Fact]
    public void TheStretcherPutsAKnownTransient_WhereTheRenderGeometryExpectsIt()
    {
        // MEASURED before the fix: 0.0 ms at 0% (the unstretched path bypasses the stretcher), but -6.9 to
        // -9.8 ms at every other tempo — the stretched audio arriving early, by an amount that MOVES with
        // the tempo. A constant offset would shift a whole mix inaudibly; one that moves puts two decks
        // milliseconds apart, which is a flam on the kick and is what "the kicks almost sit" sounded like.
        double[] clickSeconds = { 2.0, 6.0, 10.0, 14.0 };
        WriteClickTrack(_path, lengthSeconds: 20.0, clickSeconds);

        var decoder = new BassFxRenderDecoder();
        var worst = new List<string>();

        foreach (double percent in new[] { 0.0, -1.41, 0.75, 1.45, 2.94, 4.48 })
        {
            StereoBuffer buffer = decoder.DecodeStretchedStereo(_path, SampleRate, percent);
            Assert.True(buffer.Length > 0, $"no audio at {percent}% — are the BASS natives present?");

            double factor = 1.0 + (percent / 100.0);
            foreach (double click in clickSeconds)
            {
                double expected = click / factor;
                double deltaMs = (PeakSecondsNear(buffer, expected, windowSeconds: 0.5) - expected) * 1000.0;
                Line($"{percent,8:F2} {click,8:F1} {deltaMs,10:F1} ms");
                if (Math.Abs(deltaMs) > ToleranceMs)
                    worst.Add($"{percent:F2}% at {click:F1}s: {deltaMs:F1} ms");
            }
        }

        Assert.True(worst.Count == 0, "transients land off the geometry: " + string.Join("; ", worst));
    }

    /// <summary>Two milliseconds. The correction is one number per stream, but SoundTouch's own rate is a
    /// hair off, so the residual grows slowly with position — measured at 1.7 ms fourteen seconds into the
    /// hardest stretch here, against the 7-10 ms it started from. Well under the several milliseconds a
    /// listener hears as a thickened kick; tighten this only by correcting the rate too.</summary>
    private const double ToleranceMs = 2.0;

    // The loudest sample within +/- window of where the geometry says the click landed.
    private static double PeakSecondsNear(StereoBuffer buffer, double expectedSeconds, double windowSeconds)
    {
        int from = Math.Max(0, (int)((expectedSeconds - windowSeconds) * SampleRate));
        int to = Math.Min(buffer.Length, (int)((expectedSeconds + windowSeconds) * SampleRate));
        int best = from;
        float bestValue = 0f;
        for (int i = from; i < to; i++)
        {
            float v = Math.Abs(buffer.Left[i]);
            if (v > bestValue)
            {
                bestValue = v;
                best = i;
            }
        }

        return best / (double)SampleRate;
    }

    // A mono 16-bit WAV of silence with a one-sample full-scale click at each named second.
    private static void WriteClickTrack(string path, double lengthSeconds, IReadOnlyList<double> clickSeconds)
    {
        int frames = (int)(lengthSeconds * SampleRate);
        var samples = new short[frames];
        foreach (double click in clickSeconds)
        {
            int at = (int)(click * SampleRate);
            if (at >= 0 && at < frames)
            {
                // A few samples wide so a resampler cannot smear it below the noise floor.
                for (int i = 0; i < 8 && at + i < frames; i++)
                    samples[at + i] = short.MaxValue;
            }
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        int dataBytes = samples.Length * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);                     // PCM
        writer.Write((short)1);                     // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);               // byte rate
        writer.Write((short)2);                     // block align
        writer.Write((short)16);                    // bits
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (short sample in samples)
            writer.Write(sample);
    }

    public void Dispose()
    {
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "stretch-probe.txt"), _report);
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
