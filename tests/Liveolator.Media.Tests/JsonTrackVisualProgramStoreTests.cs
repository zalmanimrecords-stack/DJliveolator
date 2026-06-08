using Liveolator.Core.Library.Visual;
using Liveolator.Core.Visuals;
using Liveolator.Core.Visuals.TrackPrograms;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonTrackVisualProgramStoreTests
{
    private const string TrackA = @"C:\Music\Artist\track-a.mp3";
    private const string TrackB = @"C:\Music\Artist\track-b.mp3";

    [Fact]
    public async Task SaveThenLoad_RoundTripsProgram()
    {
        using var dir = new TempDirectory();
        var store = new JsonTrackVisualProgramStore(dir.Path);
        TrackVisualProgram program = Program(TrackA, "program-a");

        await store.SaveAsync(program);
        TrackVisualProgram? loaded = await store.LoadAsync(TrackA);

        Assert.NotNull(loaded);
        Assert.Equal(program.Id, loaded!.Id);
        Assert.Equal(program.Track, loaded.Track);
        Assert.Equal(program.Fallback, loaded.Fallback);
        Assert.Equal(program.Cues.ToArray(), loaded.Cues.ToArray());
    }

    [Fact]
    public async Task DifferentTracks_AreStoredIndependently_AndListed()
    {
        using var dir = new TempDirectory();
        var store = new JsonTrackVisualProgramStore(dir.Path);
        await store.SaveAsync(Program(TrackB, "program-b"));
        await store.SaveAsync(Program(TrackA, "program-a"));

        IReadOnlyList<TrackVisualProgramSummary> summaries = await store.ListAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Equal(new[] { TrackA, TrackB }, summaries.Select(item => item.TrackPath));
        Assert.Equal("program-a", (await store.LoadAsync(TrackA))!.Id);
        Assert.Equal("program-b", (await store.LoadAsync(TrackB))!.Id);
    }

    [Fact]
    public async Task Delete_RemovesOnlyRequestedTrack()
    {
        using var dir = new TempDirectory();
        var store = new JsonTrackVisualProgramStore(dir.Path);
        await store.SaveAsync(Program(TrackA, "program-a"));
        await store.SaveAsync(Program(TrackB, "program-b"));

        await store.DeleteAsync(TrackA);

        Assert.Null(await store.LoadAsync(TrackA));
        Assert.NotNull(await store.LoadAsync(TrackB));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonTrackVisualProgramStore(dir.Path, message => warning = message);
        Directory.CreateDirectory(store.ProgramDirectory);
        await File.WriteAllTextAsync(store.PathFor(TrackA), "{ broken");

        Assert.Null(await store.LoadAsync(TrackA));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Load_IncompatibleVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonTrackVisualProgramStore(dir.Path, message => warning = message);
        Directory.CreateDirectory(store.ProgramDirectory);
        await File.WriteAllTextAsync(store.PathFor(TrackA), """{"Version":999,"Program":null}""");

        Assert.Null(await store.LoadAsync(TrackA));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task ConcurrentSaves_DoNotLeaveTempFiles_OrLosePrograms()
    {
        using var dir = new TempDirectory();
        var store = new JsonTrackVisualProgramStore(dir.Path);

        await Task.WhenAll(
            store.SaveAsync(Program(TrackA, "program-a")),
            store.SaveAsync(Program(TrackB, "program-b")));

        Assert.NotNull(await store.LoadAsync(TrackA));
        Assert.NotNull(await store.LoadAsync(TrackB));
        Assert.Empty(Directory.EnumerateFiles(store.ProgramDirectory, "*.tmp"));
    }

    [Fact]
    public void PathFor_UsesSafeStableHash_NotTrackFileName()
    {
        using var dir = new TempDirectory();
        var store = new JsonTrackVisualProgramStore(dir.Path);

        string first = store.PathFor(TrackA);
        string second = store.PathFor(TrackA);

        Assert.Equal(first, second);
        Assert.EndsWith(".json", first);
        Assert.DoesNotContain("track-a", first, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(store.ProgramDirectory, Path.GetDirectoryName(first));
    }

    private static TrackVisualProgram Program(string trackPath, string id)
        => new(
            id,
            new TrackReference(trackPath, 123, DateTime.UnixEpoch, "Artist", "Title", TimeSpan.FromMinutes(3)),
            new[]
            {
                new TrackVisualCue(
                    "cue-1",
                    new VisualAssetReference(VisualMediaKind.Video, @"C:\Visuals\clip.mp4", 456, DateTime.UnixEpoch),
                    TimeSpan.Zero,
                    null,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    VisualFitMode.Cover,
                    VisualPlaybackMode.Loop,
                    TransitionStyle.Cut,
                    0.8),
            },
            TrackVisualFallback.GlobalDefaultProgram);
}
