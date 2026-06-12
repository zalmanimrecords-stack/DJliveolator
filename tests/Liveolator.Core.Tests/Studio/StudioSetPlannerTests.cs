using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Core.Tests.Studio;

public class StudioSetPlannerTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(string path, string camelot, double? bpm)
        => new(
            new ScannedFile(path, 1000, T),
            bpm is null ? null : new BpmResult(bpm.Value, 0.9),
            new MusicalKey(0, KeyMode.Major, camelot, 0.9),
            TimeSpan.FromMinutes(5),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null);

    private readonly StudioSetPlanner _planner = new();

    [Fact]
    public void BuildFrom_NamesTheSet_AndSeedsFirstEntry()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var candidates = new[] { Track("a.mp3", "8B", 125) };

        StudioSet set = _planner.BuildFrom("Warmup", seed, candidates, new HarmonicSetOptions(Length: 2));

        Assert.Equal("Warmup", set.Name);
        Assert.Equal("seed.mp3", set.Entries[0].TrackPath);
    }

    [Fact]
    public void BuildFrom_FirstEntry_HasNoIncomingTransition()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var candidates = new[] { Track("a.mp3", "8B", 125) };

        StudioSet set = _planner.BuildFrom("Set", seed, candidates, new HarmonicSetOptions(Length: 2));

        Assert.Null(set.Entries[0].TransitionIn);
    }

    [Fact]
    public void BuildFrom_LaterEntries_CarryDefaultTransition()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var candidates = new[] { Track("a.mp3", "8B", 125) };

        StudioSet set = _planner.BuildFrom("Set", seed, candidates, new HarmonicSetOptions(Length: 2));

        Assert.Equal(2, set.Entries.Count);
        Assert.NotNull(set.Entries[1].TransitionIn);
        // Both tracks have tempo but no phrase cues → a tail-overlap bass swap, not a cut.
        StudioTransition transition = set.Entries[1].TransitionIn!;
        Assert.Equal(TransitionKind.BassSwap, transition.Kind);
        Assert.Equal(TransitionAnchor.TailOverlap, transition.Anchor);
    }

    [Fact]
    public void BuildFrom_OrderMatchesHarmonicSetBuilder()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var near = Track("near.mp3", "8B", 125);
        var far = Track("far.mp3", "8B", 129);

        StudioSet set = _planner.BuildFrom("Set", seed, new[] { far, near },
            new HarmonicSetOptions(Length: 2, BpmTolerance: 8));

        // HarmonicSetBuilder prefers the smallest tempo jump (near before far).
        Assert.Equal(new[] { "seed.mp3", "near.mp3" }, set.TrackPaths);
    }

    [Fact]
    public void BuildFrom_MissingTempoNeighbor_DefaultsToCut()
    {
        // Seed keyed+tempo; candidate keyed but no tempo → builder still chains it under Trend.Any,
        // and the planner must mark the unmatchable handover as a Cut.
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var noTempo = Track("a.mp3", "8B", null);

        StudioSet set = _planner.BuildFrom("Set", seed, new[] { noTempo },
            new HarmonicSetOptions(Length: 2, Trend: BpmTrend.Any));

        Assert.Equal(2, set.Entries.Count);
        Assert.Equal(TransitionKind.Cut, set.Entries[1].TransitionIn!.Kind);
    }

    [Fact]
    public void BuildFrom_BlankName_Throws()
        => Assert.Throws<ArgumentException>(() =>
            _planner.BuildFrom("  ", Track("s.mp3", "8B", 124), Array.Empty<MusicTrack>(),
                new HarmonicSetOptions(Length: 1)));
}
