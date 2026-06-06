using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonHotCueStoreTests
{
    private const int SampleRate = 44_100;
    private const string TrackA = @"C:\Music\a.wav";
    private const string TrackB = @"C:\Music\b.wav";

    private static TrackCueRecord RecordFor(string path, params (int index, long pos)[] cues)
    {
        var set = new TrackCueSet(SampleRate).SetPrimaryCue(10_000);
        foreach ((int index, long pos) in cues)
            set = set.SetHotCue(index, pos, $"cue{index}", 0x112233);
        return TrackCueRecord.FromCueSet(path, set);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsCues()
    {
        using var dir = new TempDirectory();
        var store = new JsonHotCueStore(dir.Path);

        await store.SaveAsync(RecordFor(TrackA, (0, 1000), (3, 50_000)));
        TrackCueRecord? loaded = await store.LoadAsync(TrackA);

        Assert.NotNull(loaded);
        Assert.Equal(TrackA, loaded!.TrackPath);
        Assert.Equal(10_000, loaded.PrimaryCueSamples);
        Assert.Equal(2, loaded.HotCues.Count);

        // The mapped TrackCueSet must reconstruct exactly.
        TrackCueSet set = loaded.ToCueSet();
        Assert.Equal(1000, set.RecallSamples(0));
        Assert.Equal(50_000, set.RecallSamples(3));
        Assert.Equal("cue0", set.GetHotCue(0)!.Value.Label);
        Assert.Equal(0x112233, set.GetHotCue(0)!.Value.Color);
    }

    [Fact]
    public async Task Load_UnknownTrack_ReturnsNull()
    {
        using var dir = new TempDirectory();
        var store = new JsonHotCueStore(dir.Path);

        Assert.Null(await store.LoadAsync(TrackA));
    }

    [Fact]
    public async Task Save_DifferentTracks_AreKeptIndependently()
    {
        using var dir = new TempDirectory();
        var store = new JsonHotCueStore(dir.Path);

        await store.SaveAsync(RecordFor(TrackA, (0, 1000)));
        await store.SaveAsync(RecordFor(TrackB, (1, 2000)));

        Assert.Equal(1000, (await store.LoadAsync(TrackA))!.HotCues[0].PositionSamples);
        Assert.Equal(2000, (await store.LoadAsync(TrackB))!.HotCues[0].PositionSamples);
    }

    [Fact]
    public async Task Save_SameTrackTwice_Replaces()
    {
        using var dir = new TempDirectory();
        var store = new JsonHotCueStore(dir.Path);

        await store.SaveAsync(RecordFor(TrackA, (0, 1000)));
        await store.SaveAsync(RecordFor(TrackA, (0, 9999)));

        TrackCueRecord? loaded = await store.LoadAsync(TrackA);
        Assert.Single(loaded!.HotCues);
        Assert.Equal(9999, loaded.HotCues[0].PositionSamples);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatTrack()
    {
        using var dir = new TempDirectory();
        var store = new JsonHotCueStore(dir.Path);
        await store.SaveAsync(RecordFor(TrackA, (0, 1000)));
        await store.SaveAsync(RecordFor(TrackB, (1, 2000)));

        await store.DeleteAsync(TrackA);

        Assert.Null(await store.LoadAsync(TrackA));
        Assert.NotNull(await store.LoadAsync(TrackB));
    }

    [Fact]
    public async Task Delete_UnknownTrack_IsNoOp()
    {
        using var dir = new TempDirectory();
        var store = new JsonHotCueStore(dir.Path);

        await store.DeleteAsync(TrackA); // must not throw
        Assert.Null(await store.LoadAsync(TrackA));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonHotCueStore(dir.Path, onWarning: w => warning = w);
        await File.WriteAllTextAsync(store.CuesPath, "{ not valid json");

        Assert.Null(await store.LoadAsync(TrackA));
        Assert.NotNull(warning); // never silently swallowed
    }

    [Fact]
    public async Task Load_IncompatibleVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonHotCueStore(dir.Path, onWarning: w => warning = w);
        await File.WriteAllTextAsync(store.CuesPath, "{\"Version\":999,\"Tracks\":[]}");

        Assert.Null(await store.LoadAsync(TrackA));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Save_IsAtomic_NoLeftoverTempFile()
    {
        using var dir = new TempDirectory();
        var store = new JsonHotCueStore(dir.Path);

        await store.SaveAsync(RecordFor(TrackA, (0, 1000)));

        Assert.True(File.Exists(store.CuesPath));
        Assert.False(File.Exists(store.CuesPath + ".tmp"));
    }

    [Fact]
    public async Task CueStore_IsSeparateFile_FromMusicCatalog_AndDoesNotDisturbIt()
    {
        // Backward-compatibility guard: cues live in their own file, so writing cues never touches
        // catalog.music.json and an existing catalog keeps loading unchanged.
        using var dir = new TempDirectory();
        var catalog = new JsonCatalogStore(dir.Path);
        var cues = new JsonHotCueStore(dir.Path);

        var track = TestTracks.Analyzed("a.wav", 120, 0, Liveolator.Core.Analysis.Key.KeyMode.Major);
        await catalog.SaveMusicAsync(new[] { track });
        await cues.SaveAsync(RecordFor(TrackA, (0, 1000)));

        Assert.NotEqual(catalog.MusicCatalogPath, cues.CuesPath);

        IReadOnlyList<MusicTrack> reloaded = await catalog.LoadMusicAsync();
        Assert.Single(reloaded); // catalog still valid and unchanged by cue persistence
        Assert.NotNull(await cues.LoadAsync(TrackA));
    }
}
