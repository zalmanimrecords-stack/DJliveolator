using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using System.Linq;
using Xunit;

namespace Liveolator.Core.Tests.Playlist;

/// <summary>
/// The owner's rule: genre and BPM FILTER, key only ORDERS what already passed. Measured against the
/// owner's catalog, 73% of tracks carry no genre at all, so every test here that covers missing data
/// is covering the common case, not an edge case.
/// </summary>
public class TrackSuggestionRuleTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(
        string path,
        double? bpm = 140,
        string? genre = "Psytrance",
        string? camelot = "8A",
        MediaAnalysisStatus status = MediaAnalysisStatus.Ok)
        => new(
            new ScannedFile(path, 1000, T),
            bpm is null ? null : new BpmResult(bpm.Value, 0.9),
            camelot is null ? null : new MusicalKey(0, KeyMode.Major, camelot, 0.9),
            TimeSpan.FromMinutes(6),
            TrackCues.None,
            status,
            null,
            genre is null ? null : new TrackMetadata(null, null, null, null, genre, null, null, null, null, null, null, null));

    private static string[] Paths(IEnumerable<TrackSuggestion> s) => s.Select(x => x.Track.File.Path).ToArray();

    private readonly TrackSuggestionRule _rule = new();

    // ---------- the owner's decision, as an executable assertion ----------

    [Fact]
    public void Key_NeverPromotesACandidateThatFailedTheGenreOrBpmFilter()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140, genre: "Psytrance", camelot: "8A");
        var candidates = new[]
        {
            // Perfect key match, but the wrong genre and far outside the tempo window.
            Track("perfect-key-wrong-everything.mp3", bpm: 174, genre: "Drum & Bass", camelot: "8A"),
            // Right genre and tempo, deliberately the worst possible key.
            Track("right-genre-bad-key.mp3", bpm: 141, genre: "Psytrance", camelot: "3B"),
        };

        var result = _rule.Suggest(seed, candidates, new TrackSuggestionOptions());

        Assert.Equal(new[] { "right-genre-bad-key.mp3" }, Paths(result));
    }

    [Fact]
    public void Key_OrdersWithinAnAlreadyPassingPool()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140, genre: "Psytrance", camelot: "8A");
        var candidates = new[]
        {
            Track("c-unrelated-key.mp3", bpm: 140, camelot: "3B"),
            Track("b-relative.mp3", bpm: 140, camelot: "8B"),
            Track("a-same-key.mp3", bpm: 140, camelot: "8A"),
        };

        var result = _rule.Suggest(seed, candidates, new TrackSuggestionOptions());

        // Identical genre and identical BPM, so key alone decides: same key, relative, then the rest.
        Assert.Equal(new[] { "a-same-key.mp3", "b-relative.mp3", "c-unrelated-key.mp3" }, Paths(result));
    }

    // ---------- filtering ----------

    [Fact]
    public void BpmOutsideToleranceIsExcluded()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140);
        var candidates = new[] { Track("in.mp3", bpm: 145), Track("out.mp3", bpm: 152) };

        var result = _rule.Suggest(seed, candidates, new TrackSuggestionOptions(BpmTolerance: 6.0));

        Assert.Equal(new[] { "in.mp3" }, Paths(result));
    }

    [Fact]
    public void ClosestTempoRanksFirst()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140);
        var candidates = new[] { Track("far.mp3", bpm: 144), Track("near.mp3", bpm: 140.5) };

        Assert.Equal(new[] { "near.mp3", "far.mp3" }, Paths(_rule.Suggest(seed, candidates, new TrackSuggestionOptions())));
    }

    [Fact]
    public void KnownGenreMismatchIsExcludedUnderStrict()
    {
        MusicTrack seed = Track("seed.mp3", genre: "Psytrance");
        var candidates = new[] { Track("match.mp3", genre: "Psy-Trance"), Track("other.mp3", genre: "Techno") };

        Assert.Equal(new[] { "match.mp3" }, Paths(_rule.Suggest(seed, candidates, new TrackSuggestionOptions())));
    }

    [Fact]
    public void SeedAndAlreadyChosenTracksAreExcluded()
    {
        MusicTrack seed = Track("seed.mp3");
        var candidates = new[] { seed, Track("already.mp3"), Track("fresh.mp3") };

        var result = _rule.Suggest(
            seed, candidates, new TrackSuggestionOptions(), exclude: new[] { "already.mp3" });

        Assert.Equal(new[] { "fresh.mp3" }, Paths(result));
    }

    [Fact]
    public void FailedTracksAreNeverSuggested()
    {
        MusicTrack seed = Track("seed.mp3");
        var candidates = new[] { Track("broken.mp3", status: MediaAnalysisStatus.Failed), Track("ok.mp3") };

        Assert.Equal(new[] { "ok.mp3" }, Paths(_rule.Suggest(seed, candidates, new TrackSuggestionOptions())));
    }

    // ---------- missing data: the common case, never an empty list ----------

    [Fact]
    public void SeedWithoutGenre_SkipsTheGenreGateEntirely()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140, genre: null);
        var candidates = new[] { Track("techno.mp3", bpm: 141, genre: "Techno"), Track("psy.mp3", bpm: 142, genre: "Psytrance") };

        var result = _rule.Suggest(seed, candidates, new TrackSuggestionOptions());

        // Neither can be judged against a seed that has no genre, so both survive and tempo decides.
        Assert.Equal(new[] { "techno.mp3", "psy.mp3" }, Paths(result));
    }

    [Fact]
    public void CandidateWithoutGenre_IsDemotedButNeverExcluded()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140, genre: "Psytrance");
        var candidates = new[]
        {
            Track("unknown-genre-closer-tempo.mp3", bpm: 140, genre: null),
            Track("genre-match-worse-tempo.mp3", bpm: 145, genre: "Psytrance"),
        };

        var result = _rule.Suggest(seed, candidates, new TrackSuggestionOptions());

        // Demotion beats tempo: a real genre match outranks an unknown even from further away —
        // but the unknown is still offered, because excluding it would empty the list on 73% of
        // this catalog.
        Assert.Equal(new[] { "genre-match-worse-tempo.mp3", "unknown-genre-closer-tempo.mp3" }, Paths(result));
    }

    [Fact]
    public void SeedWithoutBpm_SkipsTheTempoGate()
    {
        MusicTrack seed = Track("seed.mp3", bpm: null);
        var candidates = new[] { Track("a.mp3", bpm: 100), Track("b.mp3", bpm: 175) };

        Assert.Equal(2, _rule.Suggest(seed, candidates, new TrackSuggestionOptions()).Count);
    }

    [Fact]
    public void CandidateWithoutBpm_RanksLastButIsStillOffered()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140);
        var candidates = new[] { Track("no-bpm.mp3", bpm: null), Track("has-bpm.mp3", bpm: 145) };

        Assert.Equal(new[] { "has-bpm.mp3", "no-bpm.mp3" }, Paths(_rule.Suggest(seed, candidates, new TrackSuggestionOptions())));
    }

    // ---------- genre modes ----------

    [Fact]
    public void GenreOff_IgnoresGenreForBothFilteringAndOrdering()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140, genre: "Psytrance");
        var candidates = new[] { Track("techno-closer.mp3", bpm: 140, genre: "Techno"), Track("psy-further.mp3", bpm: 144, genre: "Psytrance") };

        var result = _rule.Suggest(seed, candidates, new TrackSuggestionOptions(Genre: GenreMatch.Off));

        Assert.Equal(new[] { "techno-closer.mp3", "psy-further.mp3" }, Paths(result));
    }

    [Fact]
    public void GenreLoose_KeepsMismatchesButRanksThemBelowMatches()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140, genre: "Psytrance");
        var candidates = new[] { Track("techno-closer.mp3", bpm: 140, genre: "Techno"), Track("psy-further.mp3", bpm: 144, genre: "Psytrance") };

        var result = _rule.Suggest(seed, candidates, new TrackSuggestionOptions(Genre: GenreMatch.Loose));

        Assert.Equal(new[] { "psy-further.mp3", "techno-closer.mp3" }, Paths(result));
    }

    // ---------- contract ----------

    [Fact]
    public void OrderIsTotalAndDeterministic_TiesBreakOnTitle()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140);
        // Identical on every ranking signal, so only the title tie-break can separate them.
        var candidates = new[] { Track("b.mp3", bpm: 140), Track("c.mp3", bpm: 140), Track("a.mp3", bpm: 140) };

        var first = _rule.Suggest(seed, candidates, new TrackSuggestionOptions());
        var second = _rule.Suggest(seed, candidates.Reverse().ToArray(), new TrackSuggestionOptions());

        Assert.Equal(new[] { "a.mp3", "b.mp3", "c.mp3" }, Paths(first));
        Assert.Equal(Paths(first), Paths(second));
    }

    [Fact]
    public void LimitCapsTheResult()
    {
        MusicTrack seed = Track("seed.mp3", bpm: 140);
        var candidates = Enumerable.Range(0, 50).Select(i => Track($"t{i:00}.mp3", bpm: 140)).ToArray();

        Assert.Equal(5, _rule.Suggest(seed, candidates, new TrackSuggestionOptions(Limit: 5)).Count);
    }

    [Fact]
    public void NothingInToleranceYieldsAnEmptyList_NotAnException()
        => Assert.Empty(_rule.Suggest(
            Track("seed.mp3", bpm: 140),
            new[] { Track("far.mp3", bpm: 175) },
            new TrackSuggestionOptions()));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsANonPositiveLimit(int limit)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TrackSuggestionOptions(Limit: limit).Validate());

    [Fact]
    public void Validate_RejectsANegativeTolerance()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TrackSuggestionOptions(BpmTolerance: -1).Validate());
}
