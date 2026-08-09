using Liveolator.Core.Studio;
using Liveolator.Core.Studio.Render;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

/// <summary>
/// A slice must sound exactly like that stretch of the original — same source position, same automation
/// value — because it is what gets rendered and listened to in place of the full set.
/// </summary>
public class ProjectSliceTests
{
    private static StudioClip Clip(int slot, string path, double start, double sourceIn, double sourceOut, double bpm = 0.0)
        => new(slot, path, start, TimeSpan.FromSeconds(sourceIn), TimeSpan.FromSeconds(sourceOut),
            SourceBpm: bpm, WarpEnabled: bpm > 0.0);

    private static StudioProject Project(params StudioClip[] clips)
        => new("full", 120.0, clips, Array.Empty<AutomationLane>());

    [Fact]
    public void Extract_DropsClips_OutsideTheWindow()
    {
        StudioProject project = Project(
            Clip(0, "early.mp3", 0, 0, 30),
            Clip(1, "inside.mp3", 40, 0, 30),
            Clip(0, "late.mp3", 200, 0, 30));

        StudioProject slice = ProjectSlice.Extract(project, 35, 75, "window");

        Assert.Equal(new[] { "inside.mp3" }, slice.Clips.Select(c => c.TrackPath).ToArray());
        Assert.Equal(5.0, slice.Clips[0].TimelineStartSeconds, 6);
    }

    [Fact]
    public void Extract_TrimsAClip_ThatCrossesTheWindowStart()
    {
        StudioProject project = Project(Clip(0, "a.mp3", 10, 100, 200));

        StudioProject slice = ProjectSlice.Extract(project, 40, 70, "window");

        StudioClip clip = Assert.Single(slice.Clips);
        Assert.Equal(0.0, clip.TimelineStartSeconds, 6);
        Assert.Equal(130.0, clip.SourceIn.TotalSeconds, 6);   // 30 s into the clip
        Assert.Equal(160.0, clip.SourceOut!.Value.TotalSeconds, 6);
    }

    [Fact]
    public void Extract_ConvertsTheTrim_AtTheClipsOwnReadRate()
    {
        // A warped clip advances through its source faster than the timeline, so the trim must scale.
        StudioProject project = Project(Clip(0, "a.mp3", 0, 0, 300, bpm: 60.0));  // project 120 => factor 2

        StudioProject slice = ProjectSlice.Extract(project, 10, 20, "window");

        StudioClip clip = Assert.Single(slice.Clips);
        Assert.Equal(20.0, clip.SourceIn.TotalSeconds, 6);
        Assert.Equal(40.0, clip.SourceOut!.Value.TotalSeconds, 6);
    }

    [Fact]
    public void Extract_OpensAutomation_AtItsValueOnTheWindowEdge()
    {
        // Without a keyframe at the edge, a lane would hold its first in-window value backwards and the
        // slice would open at the wrong level.
        var lane = new AutomationLane(AutomationTarget.DeckGain, 0, new[]
        {
            new AutomationKeyframe(0.0, 0.0),
            new AutomationKeyframe(100.0, 1.0),
        });
        var project = new StudioProject("full", 120.0, new[] { Clip(0, "a.mp3", 0, 0, 200) }, new[] { lane });

        StudioProject slice = ProjectSlice.Extract(project, 40, 80, "window");

        AutomationLane sliced = Assert.Single(slice.Automation);
        Assert.Equal(0.4, sliced.ValueAt(0.0), 6);
        Assert.Equal(0.6, sliced.ValueAt(20.0), 6);
    }

    [Fact]
    public void Extract_SoundsTheSame_AsTheStretchItCameFrom()
    {
        var lane = new AutomationLane(AutomationTarget.DeckGain, 1, new[]
        {
            new AutomationKeyframe(50.0, 0.0),
            new AutomationKeyframe(80.0, 1.0),
        });
        var project = new StudioProject(
            "full", 120.0,
            new[] { Clip(0, "a.mp3", 0, 0, 120), Clip(1, "b.mp3", 50, 10, 200) },
            new[] { lane });
        var full = new MixPlan(project);

        StudioProject slice = ProjectSlice.Extract(project, 45, 95, "window");
        var sliced = new MixPlan(slice);

        for (double t = 0.0; t < 50.0; t += 1.0)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                DeckMixState expected = full.EvaluateDeck(slot, 45.0 + t);
                DeckMixState actual = sliced.EvaluateDeck(slot, t);
                Assert.Equal(expected.HasAudio, actual.HasAudio);
                if (!expected.HasAudio)
                    continue;
                Assert.Equal(expected.SourcePath, actual.SourcePath);
                Assert.Equal(expected.SourceSeconds, actual.SourceSeconds, 6);
                Assert.Equal(expected.Gain, actual.Gain, 6);
            }
        }
    }

    [Fact]
    public void Extract_ReturnsAnEmptyProject_ForAnInvertedWindow()
    {
        StudioProject project = Project(Clip(0, "a.mp3", 0, 0, 100));

        Assert.Empty(ProjectSlice.Extract(project, 60, 30, "window").Clips);
    }
}
