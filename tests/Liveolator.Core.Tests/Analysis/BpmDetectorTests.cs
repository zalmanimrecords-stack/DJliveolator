using Liveolator.Core.Analysis.Bpm;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class BpmDetectorTests
{
    [Theory]
    [InlineData(120.0)]
    [InlineData(128.0)]
    [InlineData(90.0)]
    public void Detect_ClickTrain_RecoversTempo(double bpm)
    {
        const int sr = 44100;
        float[] signal = TestSignals.ClickTrain(bpm, sr, seconds: 10);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.InRange(result.Bpm, bpm - 3.0, bpm + 3.0);
        Assert.True(result.Confidence > 0, "a clear click train should yield non-zero confidence");
    }

    [Fact]
    public void Detect_AccentedFastTrack_DoesNotCollapseToHalfTempo()
    {
        const int sr = 44100;
        float[] signal = AccentedClickTrain(
            bpm: 150.0, sampleRate: sr, seconds: 12, strong: 1.0f, weak: 0.35f);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.InRange(result.Bpm, 147.0, 153.0);
    }

    [Fact]
    public void Detect_ClickOnTheBeat_AnchorsFirstBeatNearZero()
    {
        const int sr = 44100;
        float[] signal = TestSignals.ClickTrain(120.0, sr, seconds: 10);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        // Clicks start at t=0, so the first-beat anchor lands within the first analysis frame.
        Assert.InRange(result.FirstBeatSeconds, 0.0, 0.03);
    }

    [Fact]
    public void Detect_OffsetClickTrain_RecoversTheFirstBeatOffset()
    {
        const int sr = 44100;
        // First click 0.12 s in; the anchor is a within-beat offset, so it should report ~0.12 s.
        float[] signal = TestSignals.ClickTrain(120.0, sr, seconds: 10, offsetSeconds: 0.12);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.InRange(result.FirstBeatSeconds, 0.10, 0.14);
        Assert.InRange(result.FirstBeatSeconds, 0.0, 60.0 / result.Bpm); // within one beat
    }

    [Fact]
    public void Detect_KickAccentedOnDownbeat_AnchorsDownbeatThere()
    {
        const int sr = 44100;
        // Four-on-the-floor at 120 BPM with a louder kick on beat 1 of each bar: the downbeat anchor
        // should land near t=0 and report real (non-zero) confidence.
        float[] signal = KickFourOnFloor(
            bpm: 120.0, sampleRate: sr, seconds: 16, accentBeat: 0, strong: 1.0f, weak: 0.4f);

        BpmResult result = new BpmDetector().Detect(signal, sr);

        Assert.Equal(4, result.BeatsPerBar);
        Assert.InRange(result.DownbeatSeconds, 0.0, 0.06);
        Assert.True(result.DownbeatConfidence > 0.1, $"accented downbeat should be confident, was {result.DownbeatConfidence:F3}");
    }

    [Fact]
    public void Detect_TooShortSignal_ReturnsZero()
    {
        var tiny = new float[16];
        BpmResult result = new BpmDetector().Detect(tiny, 44100);
        Assert.Equal(0, result.Bpm);
        Assert.Equal(0, result.FirstBeatSeconds);
    }

    [Fact]
    public void Detect_InvalidSampleRate_Throws()
    {
        var buffer = new float[2048];
        Assert.Throws<ArgumentOutOfRangeException>(() => new BpmDetector().Detect(buffer, 0));
    }

    /// <summary>
    /// A four-on-the-floor low-frequency kick pattern: a 55 Hz tone burst on every beat, louder on the
    /// bar-relative <paramref name="accentBeat"/>, so the downbeat is recoverable from the kick band.
    /// </summary>
    private static float[] KickFourOnFloor(
        double bpm, int sampleRate, double seconds, int accentBeat, float strong, float weak)
    {
        const int beatsPerBar = 4;
        var signal = new float[(int)(sampleRate * seconds)];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        int burstLen = (int)(0.05 * sampleRate);
        double w = 2.0 * Math.PI * 55.0 / sampleRate;
        for (double position = 0, beat = 0; position < signal.Length; position += samplesPerBeat, beat++)
        {
            float amplitude = (int)beat % beatsPerBar == accentBeat ? strong : weak;
            int start = (int)position;
            for (int sample = 0; sample < burstLen && start + sample < signal.Length; sample++)
                signal[start + sample] = (float)(amplitude * Math.Sin(w * sample));
        }
        return signal;
    }

    private static float[] AccentedClickTrain(
        double bpm,
        int sampleRate,
        double seconds,
        float strong,
        float weak)
    {
        var signal = new float[(int)(sampleRate * seconds)];
        double samplesPerBeat = 60.0 / bpm * sampleRate;
        for (double position = 0, beat = 0; position < signal.Length; position += samplesPerBeat, beat++)
        {
            float amplitude = ((int)beat & 1) == 0 ? strong : weak;
            int start = (int)position;
            for (int sample = 0; sample < 16 && start + sample < signal.Length; sample++)
                signal[start + sample] = amplitude;
        }
        return signal;
    }
}
