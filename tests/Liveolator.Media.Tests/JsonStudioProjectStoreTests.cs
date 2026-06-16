using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonStudioProjectStoreTests
{
    private static string FileFor(JsonStudioProjectStore store, string cleanName)
        => Path.Combine(store.Directory, cleanName + ".json");

    private static StudioProject SampleProject() => new("Live set", 126, new[]
    {
        new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromMinutes(4)),
        new StudioClip(2, "/m/b.wav", 90, TimeSpan.FromSeconds(8), SourceOut: null),
    }, new[]
    {
        new AutomationLane(AutomationTarget.DeckGain, 2, new[]
        {
            new AutomationKeyframe(90, 0.0),
            new AutomationKeyframe(98, 1.0),
        }),
        new AutomationLane(AutomationTarget.EqLow, 0, new[] { new AutomationKeyframe(120, 0.0) }),
    });

    [Fact]
    public async Task SaveThenLoad_RoundTripsClipsAndAutomation()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);

        await store.SaveAsync(SampleProject());
        StudioProject? loaded = await store.LoadAsync("Live set");

        Assert.NotNull(loaded);
        Assert.Equal("Live set", loaded!.Name);
        Assert.Equal(126, loaded.Bpm);

        Assert.Equal(2, loaded.Clips.Count);
        Assert.Equal(2, loaded.Clips[1].DeckSlot);
        Assert.Equal(90, loaded.Clips[1].TimelineStartSeconds);
        Assert.Equal(TimeSpan.FromSeconds(8), loaded.Clips[1].SourceIn);
        Assert.Null(loaded.Clips[1].SourceOut);

        Assert.Equal(2, loaded.Automation.Count);
        AutomationLane gain = loaded.Automation[0];
        Assert.Equal(AutomationTarget.DeckGain, gain.Target);
        Assert.Equal(2, gain.DeckSlot);
        Assert.Equal(0.5, gain.ValueAt(94), 1e-9); // interpolation survives the round-trip
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsWarpFieldsAndTempoCurve()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);
        var project = new StudioProject("Warped", 140, new[]
        {
            new StudioClip(0, "/m/a.wav", 0, TimeSpan.Zero, TimeSpan.FromMinutes(4), SourceBpm: 120, WarpEnabled: true),
        }, Array.Empty<AutomationLane>(),
            new TempoCurve(new[] { new TempoKeyframe(0, 140), new TempoKeyframe(30, 150) }));

        await store.SaveAsync(project);
        StudioProject? loaded = await store.LoadAsync("Warped");

        Assert.NotNull(loaded);
        Assert.Equal(120, loaded!.Clips[0].SourceBpm);
        Assert.True(loaded.Clips[0].WarpEnabled);
        Assert.Equal(2, loaded.EffectiveTempo.Keyframes.Count);
        Assert.Equal(150, loaded.EffectiveTempo.TempoAt(30, 140), 1e-9);
    }

    [Fact]
    public async Task Load_OldProjectWithoutTempoOrWarp_DefaultsGracefully()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);
        System.IO.Directory.CreateDirectory(store.Directory);
        // A v1-shaped file with no Tempo and clips lacking SourceBpm/WarpEnabled.
        await File.WriteAllTextAsync(FileFor(store, "Old"),
            "{\"Version\":1,\"Name\":\"Old\",\"Bpm\":120,\"Clips\":[{\"DeckSlot\":0,\"TrackPath\":\"/m/a.wav\"," +
            "\"TimelineStartSeconds\":0,\"SourceIn\":\"00:00:00\",\"SourceOut\":null}],\"Automation\":[]}");

        StudioProject? loaded = await store.LoadAsync("Old");

        Assert.NotNull(loaded);
        Assert.Equal(0, loaded!.Clips[0].SourceBpm);     // defaulted
        Assert.False(loaded.Clips[0].WarpEnabled);       // defaulted
        Assert.Empty(loaded.EffectiveTempo.Keyframes);   // null → Empty
        Assert.Equal(120, loaded.EffectiveTempo.TempoAt(5, loaded.Bpm), 1e-9);
    }

    [Fact]
    public async Task Enums_AreWrittenAsStrings()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);

        await store.SaveAsync(SampleProject());
        string json = await File.ReadAllTextAsync(FileFor(store, "Live set"));

        Assert.Contains("\"DeckGain\"", json);
        Assert.Contains("\"EqLow\"", json);
    }

    [Fact]
    public async Task List_ReturnsSavedNames_Sorted()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);
        await store.SaveAsync(StudioProject.Empty("Peak"));
        await store.SaveAsync(StudioProject.Empty("Closing"));

        Assert.Equal(new[] { "Closing", "Peak" }, await store.ListAsync());
    }

    [Fact]
    public async Task Delete_RemovesProject()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);
        await store.SaveAsync(StudioProject.Empty("Temp"));

        await store.DeleteAsync("Temp");

        Assert.Null(await store.LoadAsync("Temp"));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Load_Missing_ReturnsNull()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);
        Assert.Null(await store.LoadAsync("nope"));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonStudioProjectStore(dir.Path, onWarning: w => warning = w);
        await store.SaveAsync(StudioProject.Empty("Broken"));
        await File.WriteAllTextAsync(FileFor(store, "Broken"), "{ not valid json");

        Assert.Null(await store.LoadAsync("Broken"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Load_OlderVersion_ReturnsNull_AndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonStudioProjectStore(dir.Path, onWarning: w => warning = w);
        System.IO.Directory.CreateDirectory(store.Directory);
        await File.WriteAllTextAsync(FileFor(store, "Old"),
            "{\"Version\":0,\"Name\":\"Old\",\"Bpm\":120,\"Clips\":[],\"Automation\":[]}");

        Assert.Null(await store.LoadAsync("Old"));
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Save_IsAtomic_NoLeftoverTempFile()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);

        await store.SaveAsync(StudioProject.Empty("Set"));

        Assert.True(File.Exists(FileFor(store, "Set")));
        Assert.False(File.Exists(FileFor(store, "Set") + ".tmp"));
    }
}
