using Liveolator.Core.Mixer;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonStudioSetStoreTests
{
    private static string FileFor(JsonStudioSetStore store, string cleanName)
        => Path.Combine(store.Directory, cleanName + ".json");

    private static StudioSet SampleSet() => new("Warmup", new[]
    {
        new StudioEntry("/m/a.wav"),
        new StudioEntry(
            "/m/b.wav",
            InPoint: TimeSpan.FromSeconds(4),
            OutPoint: TimeSpan.FromMinutes(5),
            TransitionIn: new StudioTransition(
                TransitionKind.BassSwap, LengthBeats: 32, CrossfaderCurve.Smooth, TransitionAnchor.OutroToIntro)),
    });

    [Fact]
    public async Task SaveThenLoad_RoundTripsEntriesAndTransition()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioSetStore(dir.Path);

        await store.SaveAsync(SampleSet());
        StudioSet? loaded = await store.LoadAsync("Warmup");

        Assert.NotNull(loaded);
        Assert.Equal("Warmup", loaded!.Name);
        Assert.Equal(new[] { "/m/a.wav", "/m/b.wav" }, loaded.TrackPaths);

        StudioEntry second = loaded.Entries[1];
        Assert.Equal(TimeSpan.FromSeconds(4), second.InPoint);
        Assert.Equal(TimeSpan.FromMinutes(5), second.OutPoint);
        Assert.NotNull(second.TransitionIn);
        Assert.Equal(TransitionKind.BassSwap, second.TransitionIn!.Kind);
        Assert.Equal(32, second.TransitionIn.LengthBeats);
        Assert.Equal(CrossfaderCurve.Smooth, second.TransitionIn.Curve);
        Assert.Equal(TransitionAnchor.OutroToIntro, second.TransitionIn.Anchor);
    }

    [Fact]
    public async Task FirstEntry_TransitionInStaysNull_AfterRoundTrip()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioSetStore(dir.Path);

        await store.SaveAsync(SampleSet());
        StudioSet? loaded = await store.LoadAsync("Warmup");

        Assert.Null(loaded!.Entries[0].TransitionIn);
    }

    [Fact]
    public async Task Enums_AreWrittenAsStrings_ForForwardCompatibility()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioSetStore(dir.Path);

        await store.SaveAsync(SampleSet());
        string json = await File.ReadAllTextAsync(FileFor(store, "Warmup"));

        Assert.Contains("\"BassSwap\"", json);
        Assert.Contains("\"OutroToIntro\"", json);
        Assert.DoesNotContain("\"Kind\": 2", json); // not the integer ordinal
    }

    [Fact]
    public async Task List_ReturnsSavedNames_Sorted()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioSetStore(dir.Path);
        await store.SaveAsync(new StudioSet("Peak", new[] { new StudioEntry("/m/a.wav") }));
        await store.SaveAsync(new StudioSet("Closing", new[] { new StudioEntry("/m/b.wav") }));

        IReadOnlyList<string> names = await store.ListAsync();

        Assert.Equal(new[] { "Closing", "Peak" }, names);
    }

    [Fact]
    public async Task Delete_RemovesSet()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioSetStore(dir.Path);
        await store.SaveAsync(new StudioSet("Temp", new[] { new StudioEntry("/m/a.wav") }));

        await store.DeleteAsync("Temp");

        Assert.Null(await store.LoadAsync("Temp"));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Load_Missing_ReturnsNull()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioSetStore(dir.Path);

        Assert.Null(await store.LoadAsync("nope"));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonStudioSetStore(dir.Path, onWarning: w => warning = w);
        await store.SaveAsync(new StudioSet("Broken", new[] { new StudioEntry("/m/a.wav") }));
        await File.WriteAllTextAsync(FileFor(store, "Broken"), "{ not valid json");

        Assert.Null(await store.LoadAsync("Broken"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Load_OlderVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonStudioSetStore(dir.Path, onWarning: w => warning = w);
        System.IO.Directory.CreateDirectory(store.Directory);
        await File.WriteAllTextAsync(FileFor(store, "Old"),
            "{\"Version\":0,\"Name\":\"Old\",\"Entries\":[]}");

        Assert.Null(await store.LoadAsync("Old"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Save_IsAtomic_NoLeftoverTempFile()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioSetStore(dir.Path);

        await store.SaveAsync(new StudioSet("Set", new[] { new StudioEntry("/m/a.wav") }));

        Assert.True(File.Exists(FileFor(store, "Set")));
        Assert.False(File.Exists(FileFor(store, "Set") + ".tmp"));
    }

    [Fact]
    public async Task Save_NameWithIllegalChars_RoundTripsDisplayName()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioSetStore(dir.Path);
        var set = new StudioSet("Friday: 90s/2000s", new[] { new StudioEntry("/m/a.wav") });

        await store.SaveAsync(set);
        StudioSet? loaded = await store.LoadAsync("Friday: 90s/2000s");

        Assert.NotNull(loaded);
        Assert.Equal("Friday: 90s/2000s", loaded!.Name);
    }
}
