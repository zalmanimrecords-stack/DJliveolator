using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using Xunit;

namespace Liveolator.Core.Tests.Playlist;

public class HarmonicSetBuilderTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Builds a keyed track; Tonic/Mode are irrelevant to the builder, which keys off Camelot.</summary>
    private static MusicTrack Track(string path, string camelot, double? bpm)
        => new(
            new ScannedFile(path, 1000, T),
            bpm is null ? null : new BpmResult(bpm.Value, 0.9),
            new MusicalKey(0, KeyMode.Major, camelot, 0.9),
            TimeSpan.FromMinutes(4),
            TrackCues.None,
            MediaAnalysisStatus.Ok,
            null);

    private readonly HarmonicSetBuilder _builder = new();

    [Fact]
    public void Build_StartsWithSeed_AndExcludesSeedFromBody()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var candidates = new[] { Track("a.mp3", "8B", 125), seed };

        HarmonicSet set = _builder.Build(seed, candidates, new HarmonicSetOptions(Length: 3));

        Assert.Equal("seed.mp3", set.Entries[0].Track.File.Path);
        Assert.Null(set.Entries[0].Rationale); // seed has no incoming transition
        Assert.Single(set.Entries.Skip(1)); // only a.mp3 follows; seed is never re-used
    }

    [Fact]
    public void Build_OnlyChainsHarmonicallyCompatibleKeys()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var compatible = Track("compatible.mp3", "9B", 124);   // adjacent on B ring
        var incompatible = Track("incompatible.mp3", "2A", 124); // unrelated key

        HarmonicSet set = _builder.Build(seed, new[] { incompatible, compatible }, new HarmonicSetOptions(Length: 4));

        Assert.Equal(2, set.Count);
        Assert.Equal("compatible.mp3", set.Entries[1].Track.File.Path);
        Assert.DoesNotContain(set.Entries, e => e.Track.File.Path == "incompatible.mp3");
    }

    [Fact]
    public void Build_HonorsRequestedLength()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 120);
        var candidates = new[]
        {
            Track("a.mp3", "8B", 121),
            Track("b.mp3", "8B", 122),
            Track("c.mp3", "8B", 123),
            Track("d.mp3", "8B", 124),
        };

        HarmonicSet set = _builder.Build(seed, candidates, new HarmonicSetOptions(Length: 3));

        Assert.Equal(3, set.Count); // seed + 2, even though more compatible tracks exist
    }

    [Fact]
    public void Build_RisingTrend_NeverDecreasesTempo()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var slower = Track("slower.mp3", "8B", 120);
        var faster = Track("faster.mp3", "8B", 127);

        HarmonicSet set = _builder.Build(seed, new[] { slower, faster },
            new HarmonicSetOptions(Length: 3, BpmTolerance: 6, Trend: BpmTrend.Rising));

        Assert.Equal(2, set.Count); // only the faster track qualifies
        Assert.Equal("faster.mp3", set.Entries[1].Track.File.Path);
    }

    [Fact]
    public void Build_PrefersSmallestTempoJump()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var near = Track("near.mp3", "8B", 125);
        var far = Track("far.mp3", "8B", 129);

        HarmonicSet set = _builder.Build(seed, new[] { far, near },
            new HarmonicSetOptions(Length: 2, BpmTolerance: 8, Trend: BpmTrend.Any));

        Assert.Equal("near.mp3", set.Entries[1].Track.File.Path);
        Assert.Equal(1.0, set.Entries[1].Rationale!.BpmDelta);
    }

    [Fact]
    public void Build_StopsEarly_WhenNoCompatibleTrackRemains()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var onlyOne = Track("a.mp3", "8B", 124);
        var unrelated = Track("far.mp3", "2A", 124);

        HarmonicSet set = _builder.Build(seed, new[] { onlyOne, unrelated }, new HarmonicSetOptions(Length: 10));

        Assert.Equal(2, set.Count); // ran out of candidates before reaching length 10

        // A track the chain never picked has to come back with the rule that vetoed it, or the caller
        // reads "the set is short" and widens the pool when the answer was "that key does not fit".
        UnpickedCandidate leftover = Assert.Single(set.Unpicked);
        Assert.Equal("far.mp3", leftover.Track.File.Path);
        Assert.Equal(HarmonicVeto.NoCompatibleKey, leftover.Veto);
    }

    [Fact]
    public void Build_NamesTheTrend_AsTheVeto_WhenTheKeyWouldHaveFit()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var slower = Track("slower.mp3", "8B", 120);

        HarmonicSet set = _builder.Build(seed, new[] { slower },
            new HarmonicSetOptions(Length: 4, BpmTolerance: 6, Trend: BpmTrend.Rising));

        Assert.Single(set.Entries);
        UnpickedCandidate leftover = Assert.Single(set.Unpicked);
        Assert.Equal(HarmonicVeto.BlockedByTrend, leftover.Veto);
    }

    [Fact]
    public void Build_MarksTheLeftovers_Untried_WhenTheLengthCapStoppedIt()
    {
        // The cap is not a veto: nothing was asked of these two, so claiming a reason for them would be
        // a guess. They are reported as untried instead.
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var candidates = new[]
        {
            Track("a.mp3", "8B", 124),
            Track("b.mp3", "8B", 124),
            Track("c.mp3", "8B", 124),
        };

        HarmonicSet set = _builder.Build(seed, candidates, new HarmonicSetOptions(Length: 2));

        Assert.Equal(2, set.Count);
        Assert.Equal(2, set.Unpicked.Count);
        Assert.All(set.Unpicked, u => Assert.Equal(HarmonicVeto.NotTried, u.Veto));
    }

    [Fact]
    public void Build_RecordsRelativeKeyRelationship()
    {
        MusicTrack seed = Track("seed.mp3", "8B", 124);
        var relative = Track("relative.mp3", "8A", 124); // same number, switched letter

        HarmonicSet set = _builder.Build(seed, new[] { relative }, new HarmonicSetOptions(Length: 2));

        Assert.Equal("relative major/minor", set.Entries[1].Rationale!.Relationship);
    }

    [Fact]
    public void Build_Throws_WhenSeedHasNoKey()
    {
        var seed = new MusicTrack(new ScannedFile("seed.mp3", 1000, T), null, null,
            null, TrackCues.None, MediaAnalysisStatus.Failed, "no key");

        Assert.Throws<ArgumentException>(() =>
            _builder.Build(seed, Array.Empty<MusicTrack>(), new HarmonicSetOptions(Length: 2)));
    }
}
