using Liveolator.Core.Library.Visual;
using Liveolator.Core.Visuals;
using Liveolator.Core.Visuals.TrackPrograms;
using Xunit;

namespace Liveolator.Core.Tests.Visuals.TrackPrograms;

public class TrackVisualProgramTests
{
    [Fact]
    public void Constructor_orders_cues_by_track_start()
    {
        TrackVisualCue later = Cue("later", seconds: 20);
        TrackVisualCue earlier = Cue("earlier", seconds: 5);

        var program = Program(later, earlier);

        Assert.Equal(new[] { "earlier", "later" }, program.Cues.Select(cue => cue.Id));
    }

    [Fact]
    public void Constructor_rejects_overlapping_cues()
    {
        TrackVisualCue first = Cue("first", seconds: 0, endSeconds: 10);
        TrackVisualCue overlap = Cue("overlap", seconds: 9, endSeconds: 12);

        Assert.Throws<ArgumentException>(() => Program(first, overlap));
    }

    [Fact]
    public void Constructor_rejects_source_range_with_end_before_start()
    {
        Assert.Throws<ArgumentException>(
            () => new TrackVisualCue(
                "bad",
                Asset("clip.mp4", VisualMediaKind.Video),
                TimeSpan.Zero,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(3),
                VisualFitMode.Cover,
                VisualPlaybackMode.Loop,
                TransitionStyle.Cut,
                1));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_rejects_opacity_outside_unit_range(double opacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Cue("bad-opacity", seconds: 0, opacity: opacity));
    }

    private static TrackVisualProgram Program(params TrackVisualCue[] cues)
        => new(
            "program-a",
            new TrackReference("track.mp3", 123, DateTime.UnixEpoch, "Artist", "Title", TimeSpan.FromMinutes(3)),
            cues,
            TrackVisualFallback.GlobalDefaultProgram);

    private static TrackVisualCue Cue(
        string id,
        double seconds,
        double? endSeconds = null,
        double opacity = 1)
        => new(
            id,
            Asset(id + ".png", VisualMediaKind.Image),
            TimeSpan.FromSeconds(seconds),
            endSeconds is null ? null : TimeSpan.FromSeconds(endSeconds.Value),
            null,
            null,
            VisualFitMode.Cover,
            VisualPlaybackMode.Loop,
            TransitionStyle.Cut,
            opacity);

    private static VisualAssetReference Asset(string path, VisualMediaKind kind)
        => new(kind, path, 456, DateTime.UnixEpoch);
}
