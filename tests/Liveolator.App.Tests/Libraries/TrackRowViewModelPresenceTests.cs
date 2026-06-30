using Liveolator.App.Features.Libraries;
using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.App.Tests.Libraries;

/// <summary>The per-component presence booleans that drive the row analysis badges (BPM/KEY/CUE/GEN/STR).</summary>
public sealed class TrackRowViewModelPresenceTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(
        double? bpm = null, string? camelot = null, string? genre = null, SongStructure? structure = null)
    {
        TrackMetadata? meta = genre is null
            ? null
            : new TrackMetadata(null, null, null, null, genre, null, null, null, null, null, null, null);
        BpmResult? bpmResult = bpm is { } b ? new BpmResult(b, 0.9) : null;
        MusicalKey? key = camelot is null ? null : new MusicalKey(0, KeyMode.Major, camelot, 0.9);
        return new MusicTrack(
            new ScannedFile("/music/x.wav", 1000, T), bpmResult, key,
            TimeSpan.FromSeconds(120), TrackCues.None, MediaAnalysisStatus.Ok, null, meta, Structure: structure);
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
}
