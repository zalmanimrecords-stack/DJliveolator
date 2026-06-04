using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class TrackStatusPolicyTests
{
    private static TrackAnalysisResult Result(double bpmConf, double keyConf) =>
        new(new BpmResult(128, bpmConf),
            new MusicalKey(0, KeyMode.Major, "8B", keyConf),
            TimeSpan.FromMinutes(5),
            TrackCues.None);

    [Fact]
    public void For_HighConfidence_IsOk()
        => Assert.Equal(MediaAnalysisStatus.Ok, TrackStatusPolicy.For(Result(0.8, 0.9)));

    [Fact]
    public void For_LowBpmConfidence_IsPartial()
        => Assert.Equal(MediaAnalysisStatus.PartiallyAnalyzed, TrackStatusPolicy.For(Result(0.01, 0.9)));

    [Fact]
    public void For_LowKeyConfidence_IsPartial()
        => Assert.Equal(MediaAnalysisStatus.PartiallyAnalyzed, TrackStatusPolicy.For(Result(0.8, 0.2)));
}
