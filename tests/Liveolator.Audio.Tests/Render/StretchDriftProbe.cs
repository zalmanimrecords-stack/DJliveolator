using Liveolator.Audio.Render;
using Xunit.Abstractions;

namespace Liveolator.Audio.Tests.Render;

/// <summary>
/// The fixed-latency correction in <see cref="BassFxRenderDecoder"/> is one number per stream, so anything
/// left is a RATE error, and a rate error grows with position. StretchLatencyProbe only reaches 14 seconds;
/// real clips in a set run five to ten minutes. This measures the same transient geometry out to that
/// length, so "the warp smears the sound" can be answered with a number instead of an opinion.
/// <para>MEASURED: under 1.7 ms at every warp the arranger produces, and NOT growing with position -- the
/// scatter is the stretcher's transient smear, not a rate error. This asserts that, so a future change to
/// the stretch path cannot quietly reintroduce the drift the fixed-latency correction was added to kill.</para>
/// </summary>
public sealed class StretchDriftProbe : IDisposable
{
    private const int SampleRate = 44_100;
    private const double TrackSeconds = 360.0;   // six minutes — a normal psytrance record

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"liveolator-drift-probe-{Guid.NewGuid():N}.wav");
    private readonly ITestOutputHelper _out;

    public StretchDriftProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    public void DriftOverAFullLengthClip()
    {
        double[] clicks = { 10.0, 60.0, 120.0, 180.0, 240.0, 300.0, 350.0 };
        WriteClickTrack(_path, TrackSeconds, clicks);

        var decoder = new BassFxRenderDecoder();
        var worst = new List<string>();
        _out.WriteLine("  warp%   click_s    delta_ms      ms/min");
        _out.WriteLine("  ---------------------------------------");

        // The warps this set actually used, plus the extremes the arranger allows.
        foreach (double percent in new[] { -5.48, -4.83, -2.82, -1.43, 1.45 })
        {
            StereoBuffer buffer = decoder.DecodeStretchedStereo(_path, SampleRate, percent);
            Assert.True(buffer.Length > 0, $"no audio at {percent}% — are the BASS natives present?");

            double factor = 1.0 + (percent / 100.0);
            double firstDelta = 0.0, lastDelta = 0.0, firstAt = 0.0, lastAt = 0.0;

            var offenders = new List<string>();
            foreach (double click in clicks)
            {
                double expected = click / factor;
                if (expected + 1.0 >= buffer.Length / (double)SampleRate) continue;

                double deltaMs = (PeakSecondsNear(buffer, expected, 1.0) - expected) * 1000.0;
                if (firstAt == 0.0) { firstAt = expected; firstDelta = deltaMs; }
                lastAt = expected; lastDelta = deltaMs;

                _out.WriteLine($"{percent,7:F2} {click,9:F0} {deltaMs,11:F2}");
                if (Math.Abs(deltaMs) > ToleranceMs)
                    offenders.Add($"{percent:F2}% at {click:F0}s: {deltaMs:F2} ms");
            }

            double minutes = (lastAt - firstAt) / 60.0;
            double slope = minutes > 0 ? (lastDelta - firstDelta) / minutes : 0.0;
            _out.WriteLine($"{percent,7:F2} {"SLOPE",9} {"",11} {slope,11:F2}");
            _out.WriteLine("");
            worst.AddRange(offenders);
        }

        Assert.True(worst.Count == 0, "warped audio drifts off the render geometry: " + string.Join("; ", worst));
    }

    /// <summary>Two and a half milliseconds, matching StretchLatencyProbe's budget with a little room for the
    /// wider search window a six-minute file needs. Worst measured is 1.67 ms.</summary>
    private const double ToleranceMs = 2.5;

    private static double PeakSecondsNear(StereoBuffer buffer, double expectedSeconds, double windowSeconds)
    {
        int from = Math.Max(0, (int)((expectedSeconds - windowSeconds) * SampleRate));
        int to = Math.Min(buffer.Length, (int)((expectedSeconds + windowSeconds) * SampleRate));
        int best = from;
        float bestValue = 0f;
        for (int i = from; i < to; i++)
        {
            float v = Math.Abs(buffer.Left[i]);
            if (v > bestValue) { bestValue = v; best = i; }
        }
        return best / (double)SampleRate;
    }

    private static void WriteClickTrack(string path, double lengthSeconds, IReadOnlyList<double> clickSeconds)
    {
        int frames = (int)(lengthSeconds * SampleRate);
        var samples = new short[frames];
        foreach (double click in clickSeconds)
        {
            int at = (int)(click * SampleRate);
            if (at >= 0 && at < frames) samples[at] = short.MaxValue;
        }

        using var writer = new BinaryWriter(File.Create(path));
        int dataBytes = frames * 2;
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        foreach (short s in samples) writer.Write(s);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }
}
