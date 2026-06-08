using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.Media.Tests;

public sealed class JsonDeckSessionStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTripsBothDecks()
    {
        using var dir = new TempDirectory();
        var store = new JsonDeckSessionStore(dir.Path);
        DeckSessionState[] decks =
        [
            new(0, "/m/a.wav", 128, 0.12),
            new(1, "/m/b.wav", 132, 0.25),
        ];

        await store.SaveAsync(decks);
        IReadOnlyList<DeckSessionState>? loaded = await store.LoadAsync();

        Assert.Equal(decks, loaded);
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsNullAndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonDeckSessionStore(dir.Path, message => warning = message);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.Path)!);
        await File.WriteAllTextAsync(store.Path, "{ broken");

        Assert.Null(await store.LoadAsync());
        Assert.NotNull(warning);
    }

    [Fact]
    public async Task Load_FiltersInvalidEntries()
    {
        using var dir = new TempDirectory();
        var store = new JsonDeckSessionStore(dir.Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(store.Path)!);
        await File.WriteAllTextAsync(
            store.Path,
            """
            {
              "Version": 1,
              "Decks": [
                { "Slot": -1, "TrackPath": "/m/bad.wav" },
                { "Slot": 0, "TrackPath": "" },
                { "Slot": 1, "TrackPath": "/m/good.wav", "Bpm": 120 }
              ]
            }
            """);

        DeckSessionState loaded = Assert.Single((await store.LoadAsync())!);

        Assert.Equal(1, loaded.Slot);
        Assert.Equal("/m/good.wav", loaded.TrackPath);
    }
}
