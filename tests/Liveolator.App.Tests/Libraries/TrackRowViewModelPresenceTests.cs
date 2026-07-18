using Liveolator.App.Features.Libraries;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Tests.Libraries;

/// <summary>The per-component presence booleans that drive the row analysis badges (BPM/KEY/CUE/GEN/STR).</summary>
public sealed class TrackRowViewModelPresenceTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(
        double? bpm = null, string? camelot = null, string? genre = null, SongStructure? structure = null,
        double? onlineBpm = null, BpmProvenance provenance = BpmProvenance.Unknown)
    {
        TrackMetadata? meta = genre is null
            ? null
            : new TrackMetadata(null, null, null, null, genre, null, null, null, null, null, null, null);
        BpmResult? bpmResult = bpm is { } b ? new BpmResult(b, 0.9) : null;
        MusicalKey? key = camelot is null ? null : new MusicalKey(0, KeyMode.Major, camelot, 0.9);
        return new MusicTrack(
            new ScannedFile("/music/x.wav", 1000, T), bpmResult, key,
            TimeSpan.FromSeconds(120), TrackCues.None, MediaAnalysisStatus.Ok, null, meta, Structure: structure,
            OnlineBpm: onlineBpm, OnlineBpmSource: onlineBpm is null ? null : "GetSongBPM",
            BpmProvenance: provenance);
    }

    [Fact]
    public void Absent_components_read_false()
    {
        var row = new TrackRowViewModel(Track());

        Assert.False(row.HasBpm);
        Assert.False(row.HasKey);
        Assert.False(row.HasGenre);
        Assert.False(row.HasStructure);
        Assert.False(row.HasCues);
    }

    [Fact]
    public void Present_components_read_true()
    {
        var row = new TrackRowViewModel(
            Track(bpm: 128, camelot: "8A", genre: "House",
                structure: new SongStructure(Array.Empty<SongSection>(), "test")),
            hasCues: true);

        Assert.True(row.HasBpm);
        Assert.True(row.HasKey);
        Assert.True(row.HasGenre);
        Assert.True(row.HasStructure);
        Assert.True(row.HasCues);
    }

    [Fact]
    public void Zero_bpm_is_not_present()
        => Assert.False(new TrackRowViewModel(Track(bpm: 0)).HasBpm);

    [Fact]
    public void Blank_genre_is_not_present()
        => Assert.False(new TrackRowViewModel(Track(genre: "   ")).HasGenre);

    // --- BPM conflict flag (local detection vs online cross-check) ---

    [Fact]
    public void Conflicted_track_flags_and_paints_the_bpm_badge_red()
    {
        var row = new TrackRowViewModel(
            Track(bpm: 128, onlineBpm: 174, provenance: BpmProvenance.Conflicted));

        Assert.True(row.IsBpmConflicted);
        Assert.Equal("Red", row.BpmBadgeToken);
        // Tooltip names both values + the source (GetSongBPM attribution is contractual).
        Assert.Contains("128.0", row.BpmBadgeTip);
        Assert.Contains("174.0", row.BpmBadgeTip);
        Assert.Contains("GetSongBPM", row.BpmBadgeTip);
    }

    [Fact]
    public void CrossChecked_track_keeps_the_normal_badge_but_says_so_in_the_tip()
    {
        var row = new TrackRowViewModel(
            Track(bpm: 128, onlineBpm: 128, provenance: BpmProvenance.CrossChecked));

        Assert.False(row.IsBpmConflicted);
        Assert.Equal("Accent", row.BpmBadgeToken); // silence in the list — no green wallpaper
        Assert.Contains("GetSongBPM", row.BpmBadgeTip);
        Assert.Contains("128.0", row.OnlineBpmDetail);
    }

    [Fact]
    public void Unchecked_track_has_plain_badge_states()
    {
        var row = new TrackRowViewModel(Track(bpm: 128));

        Assert.False(row.IsBpmConflicted);
        Assert.Equal("Accent", row.BpmBadgeToken);
        Assert.Equal(string.Empty, row.OnlineBpmDetail);

        Assert.Equal("Faint", new TrackRowViewModel(Track()).BpmBadgeToken); // no BPM at all
    }

    [Fact]
    public void Dismissed_conflict_is_no_longer_flagged()
    {
        var row = new TrackRowViewModel(
            Track(bpm: 128, onlineBpm: 174, provenance: BpmProvenance.LocalConfirmed));

        Assert.False(row.IsBpmConflicted);
        Assert.Equal("Accent", row.BpmBadgeToken);
    }
}
