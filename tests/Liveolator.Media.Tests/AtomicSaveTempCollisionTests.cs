using Liveolator.Core.Audio.Effects;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Studio;
using Xunit;
using CorePlaylist = Liveolator.Core.Playlist.Playlist;

namespace Liveolator.Media.Tests;

/// <summary>
/// Every JSON store writes to a temp file then moves it over the live file. The temp name must be
/// UNIQUE per write: the desktop app and the <c>liveolator-mcp</c> server both write playlists and
/// studio projects, so two processes sharing one fixed <c>&lt;path&gt;.tmp</c> collide — and a temp
/// left behind by a killed process blocks the next save outright. This suite reproduces that as a
/// LOCKED stale temp sitting on the fixed path: a store that still uses the fixed name cannot open
/// it and throws; one using a unique name is unaffected.
/// </summary>
public sealed class AtomicSaveTempCollisionTests
{
    // A stale temp another (still-live or killed) writer owns, on the fixed "<path>.tmp" slot.
    private static FileStream LockStaleTemp(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None);
    }

    // The single .json the store wrote, whatever naming scheme it uses internally.
    private static string SoleJsonFile(string directory)
        => Assert.Single(Directory.EnumerateFiles(directory, "*.json"));

    [Fact]
    public async Task Playlist_SaveSucceeds_WhileStaleTempIsLocked()
    {
        using var dir = new TempDirectory();
        var store = new JsonPlaylistStore(dir.Path);
        await store.SaveAsync(new CorePlaylist("Warmup", new[] { "/m/a.wav" }));
        using FileStream stale = LockStaleTemp(SoleJsonFile(store.Directory));

        await store.SaveAsync(new CorePlaylist("Warmup", new[] { "/m/a.wav", "/m/b.wav" }));

        CorePlaylist? loaded = await store.LoadAsync("Warmup");
        Assert.Equal(new[] { "/m/a.wav", "/m/b.wav" }, loaded!.TrackPaths);
    }

    [Fact]
    public async Task StudioProject_SaveSucceeds_WhileStaleTempIsLocked()
    {
        using var dir = new TempDirectory();
        var store = new JsonStudioProjectStore(dir.Path);
        await store.SaveAsync(new StudioProject("Live set", 126, [], []));
        using FileStream stale = LockStaleTemp(SoleJsonFile(store.Directory));

        await store.SaveAsync(new StudioProject("Live set", 140, [], []));

        StudioProject? loaded = await store.LoadAsync("Live set");
        Assert.Equal(140, loaded!.Bpm);
    }

    [Fact]
    public async Task DeckSession_SaveSucceeds_WhileStaleTempIsLocked()
    {
        using var dir = new TempDirectory();
        var store = new JsonDeckSessionStore(dir.Path);
        await store.SaveAsync([new DeckSessionState(0, "/m/a.wav", 128, 0)]);
        using FileStream stale = LockStaleTemp(store.Path);

        await store.SaveAsync([new DeckSessionState(0, "/m/b.wav", 132, 0)]);

        DeckSessionState loaded = Assert.Single((await store.LoadAsync())!);
        Assert.Equal("/m/b.wav", loaded.TrackPath);
    }

    [Fact]
    public async Task Settings_SaveSucceeds_WhileStaleTempIsLocked()
    {
        using var dir = new TempDirectory();
        var store = new JsonSettingsStore(dir.Path);
        await store.SaveAsync(new AppSettings());
        using FileStream stale = LockStaleTemp(store.FilePath);

        await store.SaveAsync(new AppSettings { Legal = new LegalSettings(3) });

        AppSettings loaded = await store.LoadAsync();
        Assert.Equal(3, loaded.Legal.AcceptedTermsVersion);
    }

    [Fact]
    public async Task AudioEffectRacks_SaveSucceeds_WhileStaleTempIsLocked()
    {
        using var dir = new TempDirectory();
        var store = new JsonAudioEffectRackStateStore(dir.Path);
        await store.SaveAsync([new AudioEffectRackState(AudioEffectRackSlot.DeckA, [], LatencySamples: 0)]);
        using FileStream stale = LockStaleTemp(store.FilePath);

        await store.SaveAsync([new AudioEffectRackState(AudioEffectRackSlot.DeckA, [], LatencySamples: 64)]);

        AudioEffectRackState loaded = Assert.Single(await store.LoadAsync());
        Assert.Equal(64, loaded.LatencySamples);
    }
}
