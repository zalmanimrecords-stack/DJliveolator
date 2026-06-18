using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class HarmonicAutoArrangerTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(4);

    /// <summary>A keyed, analyzed track. Pass duration explicitly (incl. null) to override the 4-min default.</summary>
    private static MusicTrack Track(string path, string? camelot, double? bpm)
        => Track(path, camelot, bpm, DefaultDuration);

    private static MusicTrack Track(string path, string? camelot, double? bpm, TimeSpan? duration)
        => new(
            new ScannedFile(path, 1000, T),
            bpm is null ? null : new BpmResult(bpm.Value, 0.9),
            camelot is null ? null : new MusicalKey(0, KeyMode.Major, camelot, 0.9),
            duration,
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null);

    private readonly HarmonicAutoArranger _arranger = new();
    private readonly HarmonicSetBuilder _builder = new();

    [Fact]
    public void Arrange_OrdersClips_LikeHarmonicSetBuilder()
    {
        var tracks = new[]
        {
            Track("seed.mp3", "8B", 120),
            Track("a.mp3", "8B", 121),
            Track("b.mp3", "9B", 122),
            Track("c.mp3", "8B", 123),
        };
        var harmonic = new HarmonicSetOptions(Length: tracks.Length);

        StudioProject project = _arranger.Arrange(tracks, harmonic, new AutoArrangeOptions());

        // Seed = first eligible track in input order; the body must follow the builder's chain.
        HarmonicSet expected = _builder.Build(tracks[0], tracks, harmonic);
        Assert.Equal(
            expected.Entries.Select(e => e.Track.File.Path).ToArray(),
            project.Clips.Select(c => c.TrackPath).ToArray());
    }

    [Fact]
    public void Arrange_AlternatesDeckSlots_StartingAtStartDeck()
    {
        var tracks = new[]
        {
            Track("seed.mp3", "8B", 120),
            Track("a.mp3", "8B", 121),
            Track("b.mp3", "8B", 122),
        };

        StudioProject project = _arranger.Arrange(tracks, new HarmonicSetOptions(Length: 3), new AutoArrangeOptions(StartDeckSlot: 1));

        Assert.Equal(new[] { 1, 0, 1 }, project.Clips.Select(c => c.DeckSlot).ToArray());
    }

    [Fact]
    public void Arrange_OverlapsConsecutiveClips_ByOverlapSeconds()
    {
        var tracks = new[]
        {
            Track("seed.mp3", "8B", 120, TimeSpan.FromSeconds(60)),
            Track("a.mp3", "8B", 121, TimeSpan.FromSeconds(90)),
            Track("b.mp3", "8B", 122, TimeSpan.FromSeconds(120)),
        };
        const double overlap = 8.0;

        StudioProject project = _arranger.Arrange(tracks, new HarmonicSetOptions(Length: 3), new AutoArrangeOptions(OverlapSeconds: overlap));

        // Each clip starts exactly `overlap` before the previous clip's timeline end.
        for (int i = 1; i < project.Clips.Count; i++)
        {
            double prevEnd = project.Clips[i - 1].TimelineEndSeconds!.Value;
            Assert.Equal(prevEnd - overlap, project.Clips[i].TimelineStartSeconds, precision: 6);
        }
    }

    [Fact]
    public void Arrange_SetsCrossfadeFades_ToOverlap()
    {
        var tracks = new[]
        {
            Track("seed.mp3", "8B", 120),
            Track("a.mp3", "8B", 121),
            Track("b.mp3", "8B", 122),
        };
        const double overlap = 10.0;

        StudioProject project = _arranger.Arrange(tracks, new HarmonicSetOptions(Length: 3), new AutoArrangeOptions(OverlapSeconds: overlap));

        IReadOnlyList<StudioClip> clips = project.Clips;
        // First clip: no fade in, fades out into the next.
        Assert.Equal(0.0, clips[0].FadeInSeconds);
        Assert.Equal(overlap, clips[0].FadeOutSeconds);
        // Middle clip: fades in from previous, out into next.
        Assert.Equal(overlap, clips[1].FadeInSeconds);
        Assert.Equal(overlap, clips[1].FadeOutSeconds);
        // Last clip: fades in, no fade out.
        Assert.Equal(overlap, clips[^1].FadeInSeconds);
        Assert.Equal(0.0, clips[^1].FadeOutSeconds);
    }

    [Fact]
    public void Arrange_TimelineStarts_AreMonotonicNonDecreasing()
    {
        var tracks = new[]
        {
            Track("seed.mp3", "8B", 120),
            Track("a.mp3", "8B", 121),
            Track("b.mp3", "8B", 122),
            Track("c.mp3", "8B", 123),
        };

        StudioProject project = _arranger.Arrange(tracks, new HarmonicSetOptions(Length: 4), new AutoArrangeOptions());

        double[] starts = project.Clips.Select(c => c.TimelineStartSeconds).ToArray();
        for (int i = 1; i < starts.Length; i++)
            Assert.True(starts[i] >= starts[i - 1], $"start[{i}]={starts[i]} < start[{i - 1}]={starts[i - 1]}");
    }

    [Fact]
    public void Arrange_FirstClipStartsAtZero_AndCoversFullTrack()
    {
        var track = Track("seed.mp3", "8B", 120, TimeSpan.FromSeconds(200));

        StudioProject project = _arranger.Arrange(new[] { track }, new HarmonicSetOptions(Length: 1), new AutoArrangeOptions());

        StudioClip clip = Assert.Single(project.Clips);
        Assert.Equal(0.0, clip.TimelineStartSeconds);
        Assert.Equal(TimeSpan.Zero, clip.SourceIn);
        Assert.Equal(TimeSpan.FromSeconds(200), clip.SourceOut);
    }

    [Fact]
    public void Arrange_ProjectBpm_IsFirstOrderedTrackTempo()
    {
        var tracks = new[]
        {
            Track("seed.mp3", "8B", 128),
            Track("a.mp3", "8B", 126),
        };

        StudioProject project = _arranger.Arrange(tracks, new HarmonicSetOptions(Length: 2), new AutoArrangeOptions());

        Assert.Equal(128.0, project.Bpm);
    }

    [Fact]
    public void Arrange_UsesProjectName_FromOptions()
    {
        var track = Track("seed.mp3", "8B", 120);

        StudioProject project = _arranger.Arrange(new[] { track }, new HarmonicSetOptions(Length: 1), new AutoArrangeOptions(ProjectName: "My Set"));

        Assert.Equal("My Set", project.Name);
    }

    [Fact]
    public void Arrange_EmptyInput_YieldsEmptyProject()
    {
        StudioProject project = _arranger.Arrange(Array.Empty<MusicTrack>(), new HarmonicSetOptions(Length: 4), new AutoArrangeOptions());

        Assert.Empty(project.Clips);
        Assert.Empty(project.Automation);
    }

    [Fact]
    public void Arrange_NoKeyedTracks_YieldsEmptyProject()
    {
        // The builder needs a keyed seed; with none, there is nothing to arrange.
        var tracks = new[] { Track("a.mp3", camelot: null, bpm: 120), Track("b.mp3", camelot: null, bpm: 121) };

        StudioProject project = _arranger.Arrange(tracks, new HarmonicSetOptions(Length: 2), new AutoArrangeOptions());

        Assert.Empty(project.Clips);
    }

    [Fact]
    public void Arrange_UnknownDuration_DoesNotCrash_AndLeavesEndOpen()
    {
        var tracks = new[]
        {
            Track("seed.mp3", "8B", 120, duration: null),  // open-ended clip
            Track("a.mp3", "8B", 121, TimeSpan.FromSeconds(90)),
        };

        StudioProject project = _arranger.Arrange(tracks, new HarmonicSetOptions(Length: 2), new AutoArrangeOptions(OverlapSeconds: 8.0));

        Assert.Equal(2, project.Clips.Count);
        // Unknown-duration clip has an open out point and therefore no computable timeline end.
        Assert.Null(project.Clips[0].SourceOut);
        Assert.Null(project.Clips[0].TimelineEndSeconds);
        // The following clip is still placed; starts must remain monotonic non-decreasing.
        Assert.True(project.Clips[1].TimelineStartSeconds >= project.Clips[0].TimelineStartSeconds);
    }

    [Fact]
    public void Arrange_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => _arranger.Arrange(null!, new HarmonicSetOptions(Length: 1), new AutoArrangeOptions()));
        Assert.Throws<ArgumentNullException>(() => _arranger.Arrange(Array.Empty<MusicTrack>(), null!, new AutoArrangeOptions()));
        Assert.Throws<ArgumentNullException>(() => _arranger.Arrange(Array.Empty<MusicTrack>(), new HarmonicSetOptions(Length: 1), null!));
    }
}
