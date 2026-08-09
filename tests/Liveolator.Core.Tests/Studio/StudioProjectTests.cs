using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class StudioProjectTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void Empty_HasNameDefaultTempoAndNothingElse()
    {
        StudioProject p = StudioProject.Empty("My set");

        Assert.Equal("My set", p.Name);
        Assert.Equal(StudioProject.DefaultBpm, p.Bpm, Tol);
        Assert.Empty(p.Clips);
        Assert.Empty(p.Automation);
        Assert.Equal(0, p.DurationSeconds, Tol);
    }

    [Fact]
    public void Clip_KnownLength_ComputesTimelineEndAndDuration()
    {
        var clip = new StudioClip(
            DeckSlot: 0, TrackPath: "/m/a.wav",
            TimelineStartSeconds: 8,
            SourceIn: TimeSpan.FromSeconds(10),
            SourceOut: TimeSpan.FromSeconds(40)); // 30s of source

        Assert.Equal(TimeSpan.FromSeconds(30), clip.SourceDuration);
        Assert.Equal(38, clip.TimelineEndSeconds!.Value, Tol); // 8 + 30
    }

    [Fact]
    public void Clip_OpenEnded_HasNullDurationAndEnd()
    {
        var clip = new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, SourceOut: null);
        Assert.Null(clip.SourceDuration);
        Assert.Null(clip.TimelineEndSeconds);
    }

    [Fact]
    public void DurationSeconds_IsLatestClipEnd()
    {
        var p = new StudioProject("p", 124, new[]
        {
            new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromSeconds(20)),   // ends 20
            new StudioClip(1, "/m/b.wav", 16, TimeSpan.Zero, TimeSpan.FromSeconds(30)),  // ends 46
        }, Array.Empty<AutomationLane>());

        Assert.Equal(46, p.DurationSeconds, Tol);
    }
}
