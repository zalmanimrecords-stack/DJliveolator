using Liveolator.Core.Analysis;
using Xunit;

namespace Liveolator.Core.Tests.Analysis;

public class SilenceCueDetectorTests
{
    [Fact]
    public void Detect_TrimsLeadingAndTrailingSilence()
    {
        const int sr = 44100;
        var lead = new float[sr / 2];                       // 0.5 s silence
        var tone = TestSignals.Sine(440, sr, seconds: 1.0); // 1.0 s audible
        var tail = new float[sr / 2];                       // 0.5 s silence
        var signal = lead.Concat(tone).Concat(tail).ToArray();

        TrackCues cues = new SilenceCueDetector().Detect(signal, sr);

        Assert.NotNull(cues.IntroStart);
        Assert.NotNull(cues.OutroEnd);
        Assert.InRange(cues.IntroStart!.Value.TotalSeconds, 0.4, 0.6);
        Assert.InRange(cues.OutroEnd!.Value.TotalSeconds, 1.4, 1.6);
    }

    [Fact]
    public void Detect_Silence_ReturnsNone()
    {
        TrackCues cues = new SilenceCueDetector().Detect(new float[44100], 44100);
        Assert.Equal(TrackCues.None, cues);
    }
}
