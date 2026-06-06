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
}
