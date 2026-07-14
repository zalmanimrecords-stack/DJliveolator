using Liveolator.Core.Library;
using Liveolator.Core.Library.Doctor;
using Xunit;

namespace Liveolator.Media.Tests;

public class JsonMediaIdentityStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTripsIdentities()
    {
        using var dir = new TempDirectory();
        var store = new JsonMediaIdentityStore(dir.Path);
        var identity = new MediaIdentity(
            "stable",
            MediaIdentityKind.Music,
            new[] { "/music/a.mp3", "/backup/a.mp3" },
            "a.mp3",
            123,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "abc123",
            MediaAnalysisStatus.Ok,
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        await store.SaveIdentitiesAsync(new[] { identity });
        IReadOnlyList<MediaIdentity> loaded = await store.LoadIdentitiesAsync();

        MediaIdentity roundTrip = Assert.Single(loaded);
        Assert.Equal(identity.StableId, roundTrip.StableId);
        Assert.Equal(identity.Kind, roundTrip.Kind);
        Assert.Equal(identity.Paths, roundTrip.Paths);
        Assert.Equal(identity.FileName, roundTrip.FileName);
        Assert.Equal(identity.SizeBytes, roundTrip.SizeBytes);
        Assert.Equal(identity.LastModifiedUtc, roundTrip.LastModifiedUtc);
        Assert.Equal(identity.Sha256, roundTrip.Sha256);
        Assert.Equal(identity.Status, roundTrip.Status);
        Assert.Equal(identity.LastSeenUtc, roundTrip.LastSeenUtc);
    }

    [Fact]
    public async Task Load_WhenCorrupt_ReturnsEmptyAndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonMediaIdentityStore(dir.Path, w => warning = w);
        await File.WriteAllTextAsync(store.Path, "{{{");

        IReadOnlyList<MediaIdentity> loaded = await store.LoadIdentitiesAsync();

        Assert.Empty(loaded);
        Assert.Contains("unreadable", warning);
    }

    [Fact]
    public async Task Load_WhenVersionMismatch_ReturnsEmptyAndWarns()
    {
        using var dir = new TempDirectory();
        string? warning = null;
        var store = new JsonMediaIdentityStore(dir.Path, w => warning = w);
        await File.WriteAllTextAsync(store.Path, "{\"Version\":0,\"Identities\":[]}");

        IReadOnlyList<MediaIdentity> loaded = await store.LoadIdentitiesAsync();

        Assert.Empty(loaded);
        Assert.Contains("version 0", warning);
    }
}
