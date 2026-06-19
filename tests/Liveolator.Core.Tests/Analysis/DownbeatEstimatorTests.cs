using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public sealed class DownbeatEstimatorTests
{
    private const double EnvelopeRateHz = 100.0; // 1 frame = 10 ms
    private const double Bpm = 120.0;            // 1 beat = 0.5 s = 50 frames
    private const int BeatFrames = 50;
    private const int BeatsPerBar = 4;

    [Fact]
    public void Estimate_AccentOnFirstBeatOfBar_FindsDownbeatAtZero()
    {
        // Kick on every beat, but beat 1 of each bar is far stronger — a textbook downbeat.
        double[] envelope = BeatEnvelope(bars: 8, accentBeat: 0, strong: 1.0, weak: 0.25);

        DownbeatEstimate result = new DownbeatEstimator()
            .Estimate(envelope, Bpm, EnvelopeRateHz, firstBeatSeconds: 0.0);

        Assert.InRange(result.DownbeatSeconds, 0.0, 0.02);
        Assert.True(result.Confidence > 0.3, $"clear downbeat should be confident, was {result.Confidence:F3}");
    }

    [Fact]
    public void Estimate_AccentOnThirdBeatOfBar_FindsDownbeatThere()
    {
        // The strong beat is the 3rd in the bar (index 2) → downbeat sits 2 beats (1.0 s) in.
        double[] envelope = BeatEnvelope(bars: 8, accentBeat: 2, strong: 1.0, weak: 0.25);

        DownbeatEstimate result = new DownbeatEstimator()
            .Estimate(envelope, Bpm, EnvelopeRateHz, firstBeatSeconds: 0.0);

        double beatSeconds = 60.0 / Bpm;
        Assert.InRange(result.DownbeatSeconds, 2 * beatSeconds - 0.02, 2 * beatSeconds + 0.02);
    }

    [Fact]
    public void Estimate_UniformBeats_AreAmbiguous_LowConfidence()
    {
        // Four-on-the-floor with identical kicks: no beat is the downbeat, so confidence must stay low
        // (expose the ambiguity rather than inventing a downbeat).
        double[] envelope = BeatEnvelope(bars: 8, accentBeat: -1, strong: 1.0, weak: 1.0);

        DownbeatEstimate result = new DownbeatEstimator()
            .Estimate(envelope, Bpm, EnvelopeRateHz, firstBeatSeconds: 0.0);

        Assert.True(result.Confidence < 0.1, $"uniform beats should be ambiguous, was {result.Confidence:F3}");
        Assert.InRange(result.DownbeatSeconds, 0.0, BeatsPerBar * (60.0 / Bpm)); // still within one bar
    }

    [Fact]
    public void Estimate_RespectsFirstBeatOffset()
    {
        // Grid shifted 0.12 s in; downbeat stays anchored to the offset beat (accent on beat 1).
        double offset = 0.12;
        double[] envelope = BeatEnvelope(bars: 8, accentBeat: 0, strong: 1.0, weak: 0.25, offsetSeconds: offset);

        DownbeatEstimate result = new DownbeatEstimator()
            .Estimate(envelope, Bpm, EnvelopeRateHz, firstBeatSeconds: offset);

        Assert.InRange(result.DownbeatSeconds, offset - 0.02, offset + 0.02);
    }

    [Fact]
    public void Estimate_EmptyEnvelope_ReturnsZero()
    {
        DownbeatEstimate result = new DownbeatEstimator()
            .Estimate(Array.Empty<double>(), Bpm, EnvelopeRateHz, firstBeatSeconds: 0.0);

        Assert.Equal(0.0, result.DownbeatSeconds);
        Assert.Equal(0.0, result.Confidence);
    }

    /// <summary>
    /// An onset envelope of one spike per beat. <paramref name="accentBeat"/> is the bar-relative beat
    /// index (0..3) that carries <paramref name="strong"/> energy; every other beat carries
    /// <paramref name="weak"/>. -1 means all beats equal.
    /// </summary>
    private static double[] BeatEnvelope(
        int bars, int accentBeat, double strong, double weak, double offsetSeconds = 0.0)
    {
        int totalBeats = bars * BeatsPerBar;
        int offsetFrames = (int)Math.Round(offsetSeconds * EnvelopeRateHz);
        var envelope = new double[offsetFrames + totalBeats * BeatFrames + BeatFrames];
        for (int beat = 0; beat < totalBeats; beat++)
        {
            int frame = offsetFrames + beat * BeatFrames;
            bool isAccent = accentBeat >= 0 && beat % BeatsPerBar == accentBeat;
            envelope[frame] = isAccent ? strong : weak;
        }
        return envelope;
    }
}
