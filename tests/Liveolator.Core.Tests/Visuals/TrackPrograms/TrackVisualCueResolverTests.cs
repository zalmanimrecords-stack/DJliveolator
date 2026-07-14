using Liveolator.Core.Library.Visual;
using Liveolator.Core.Visuals;
using Liveolator.Core.Visuals.TrackPrograms;
using Xunit;

namespace Liveolator.Core.Tests.Visuals.TrackPrograms;

public class TrackVisualCueResolverTests
{
    [Theory]
    [InlineData(0, "first")]
    [InlineData(9.999, "first")]
    [InlineData(10, "second")]
    [InlineData(59, "second")]
    public void Resolve_uses_explicit_end_and_next_cue_boundary(double seconds, string expected)
    {
        var program = Program(
            Cue("first", 0, endSeconds: 10),
            Cue("second", 10));

        TrackVisualCue? resolved = TrackVisualCueResolver.Resolve(program, TimeSpan.FromSeconds(seconds));

        Assert.Equal(expected, resolved?.Id);
    }

    [Fact]
    public void Resolve_returns_null_before_first_cue()
    {
        var program = Program(Cue("later", 5));

        Assert.Null(TrackVisualCueResolver.Resolve(program, TimeSpan.FromSeconds(4.9)));
    }

    [Fact]
    public void Resolve_returns_null_after_explicit_final_end()
    {
        var program = Program(Cue("only", 0, endSeconds: 5));

        Assert.Null(TrackVisualCueResolver.Resolve(program, TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(2, 5)]
    [InlineData(5, 3)]
    [InlineData(7, 5)]
    [InlineData(12, 5)]
    public void SourceTime_loop_wraps_inside_selected_source_range(double trackSeconds, double expectedSourceSeconds)
    {
        TrackVisualCue cue = VideoCue(
            startSeconds: 0,
            sourceInSeconds: 3,
            sourceOutSeconds: 8,
            VisualPlaybackMode.Loop);

        TimeSpan sourceTime = TrackVisualCueResolver.ResolveSourceTime(cue, TimeSpan.FromSeconds(trackSeconds));

        Assert.Equal(expectedSourceSeconds, sourceTime.TotalSeconds, precision: 6);
    }

    [Fact]
    public void SourceTime_once_clamps_to_selected_source_end()
    {
        TrackVisualCue cue = VideoCue(
            startSeconds: 10,
            sourceInSeconds: 2,
            sourceOutSeconds: 6,
            VisualPlaybackMode.Once);

        TimeSpan sourceTime = TrackVisualCueResolver.ResolveSourceTime(cue, TimeSpan.FromSeconds(30));

        Assert.Equal(6, sourceTime.TotalSeconds, precision: 6);
    }

    private static TrackVisualProgram Program(params TrackVisualCue[] cues)
        => new(
            "program",
            new TrackReference("track.mp3", 1, DateTime.UnixEpoch, null, null, TimeSpan.FromMinutes(1)),
            cues,
            TrackVisualFallback.Transparent);

    private static TrackVisualCue Cue(string id, double startSeconds, double? endSeconds = null)
        => new(
            id,
            new VisualAssetReference(VisualMediaKind.Image, id + ".png", 1, DateTime.UnixEpoch),
            TimeSpan.FromSeconds(startSeconds),
            endSeconds is null ? null : TimeSpan.FromSeconds(endSeconds.Value),
            null,
            null,
            VisualFitMode.Contain,
            VisualPlaybackMode.Loop,
            TransitionStyle.Cut,
            1);

    private static TrackVisualCue VideoCue(
        double startSeconds,
        double sourceInSeconds,
        double sourceOutSeconds,
        VisualPlaybackMode playback)
        => new(
            "video",
            new VisualAssetReference(VisualMediaKind.Video, "video.mp4", 1, DateTime.UnixEpoch),
            TimeSpan.FromSeconds(startSeconds),
            null,
            TimeSpan.FromSeconds(sourceInSeconds),
            TimeSpan.FromSeconds(sourceOutSeconds),
            VisualFitMode.Cover,
            playback,
            TransitionStyle.Cut,
            1);
}
