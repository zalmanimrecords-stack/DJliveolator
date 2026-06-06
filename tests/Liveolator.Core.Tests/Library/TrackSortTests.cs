using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Xunit;

namespace Liveolator.Core.Tests.Library;

public class TrackSortTests
{
    private static readonly DateTime T = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Track(
        string title, double? bpm = 120, string? camelot = "8A", int durationSeconds = 240)
    {
        var meta = new TrackMetadata(title, null, null, null, null, null, null, null, null, null, null, null);
        BpmResult? bpmResult = bpm is { } b ? new BpmResult(b, 0.9) : null;
        MusicalKey? key = camelot is null ? null : new MusicalKey(0, KeyMode.Major, camelot, 0.9);
        return new MusicTrack(
            new ScannedFile($"/m/{title}.mp3", 1000, T),
            bpmResult, key,
            TimeSpan.FromSeconds(durationSeconds), TrackCues.None, MediaAnalysisStatus.Ok, null, meta);
    }

    [Fact]
    public void Bpm_ascending_orders_by_tempo()
    {
        var tracks = new[] { Track("A", bpm: 128), Track("B", bpm: 90), Track("C", bpm: 110) };

        var sorted = TrackSort.Apply(tracks, TrackSortKey.Bpm, descending: false);

        Assert.Equal(new[] { "B", "C", "A" }, sorted.Select(t => t.Title));
    }

    [Fact]
    public void Bpm_descending_reverses_order()
    {
        var tracks = new[] { Track("A", bpm: 128), Track("B", bpm: 90), Track("C", bpm: 110) };

        var sorted = TrackSort.Apply(tracks, TrackSortKey.Bpm, descending: true);

        Assert.Equal(new[] { "A", "C", "B" }, sorted.Select(t => t.Title));
    }

    [Fact]
    public void Duration_ascending_orders_shortest_first()
    {
        var tracks = new[] { Track("A", durationSeconds: 300), Track("B", durationSeconds: 120), Track("C", durationSeconds: 200) };

        var sorted = TrackSort.Apply(tracks, TrackSortKey.Duration, descending: false);

        Assert.Equal(new[] { "B", "C", "A" }, sorted.Select(t => t.Title));
    }

    [Fact]
    public void Title_ascending_is_case_insensitive_and_default()
    {
        var tracks = new[] { Track("beta"), Track("Alpha"), Track("gamma") };

        var sorted = TrackSort.Apply(tracks, TrackSortKey.Title, descending: false);

        Assert.Equal(new[] { "Alpha", "beta", "gamma" }, sorted.Select(t => t.Title));
    }

    [Fact]
    public void Key_ascending_orders_around_the_camelot_wheel_number_then_letter()
    {
        // 1A, 1B, 8A, 8B is the expected wheel order (number ascending, A before B).
        var tracks = new[]
        {
            Track("X", camelot: "8B"), Track("Y", camelot: "1A"),
            Track("Z", camelot: "8A"), Track("W", camelot: "1B"),
        };

        var sorted = TrackSort.Apply(tracks, TrackSortKey.Key, descending: false);

        Assert.Equal(new[] { "Y", "W", "Z", "X" }, sorted.Select(t => t.Title));
    }

    [Fact]
    public void Missing_values_sort_last_regardless_of_direction()
    {
        var tracks = new[] { Track("HasBpm", bpm: 120), Track("NoBpm", bpm: null) };

        var asc = TrackSort.Apply(tracks, TrackSortKey.Bpm, descending: false);
        var desc = TrackSort.Apply(tracks, TrackSortKey.Bpm, descending: true);

        Assert.Equal("NoBpm", asc[^1].Title);  // unknown tempo never leads
        Assert.Equal("NoBpm", desc[^1].Title);
    }

    [Fact]
    public void Sort_is_stable_for_equal_keys_falling_back_to_title()
    {
        var tracks = new[] { Track("Gamma", bpm: 120), Track("Alpha", bpm: 120), Track("Beta", bpm: 120) };

        var sorted = TrackSort.Apply(tracks, TrackSortKey.Bpm, descending: false);

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, sorted.Select(t => t.Title));
    }
}
