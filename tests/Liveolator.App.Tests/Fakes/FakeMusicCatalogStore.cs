using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;

namespace Liveolator.App.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IMusicCatalogStore"/> for view-model tests: records what was saved and
/// replays a seeded set on load, so persistence round-trips can be asserted with no disk access.
/// All methods complete synchronously, so a guarded fire-and-forget save settles before the test
/// inspects it (the VM uses <see cref="System.Reactive.Concurrency.ImmediateScheduler"/> in tests).
/// </summary>
public sealed class FakeMusicCatalogStore : IMusicCatalogStore
{
    private readonly IReadOnlyList<MusicTrack> _seedTracks;
    private readonly IReadOnlyList<string> _seedFolders;
    private readonly IReadOnlyList<string> _seedSampleFolders;

    public FakeMusicCatalogStore(
        IEnumerable<MusicTrack>? seedTracks = null,
        IEnumerable<string>? seedFolders = null,
        IEnumerable<string>? seedSampleFolders = null)
    {
        _seedTracks = seedTracks?.ToList() ?? new List<MusicTrack>();
        _seedFolders = seedFolders?.ToList() ?? new List<string>();
        _seedSampleFolders = seedSampleFolders?.ToList() ?? new List<string>();
    }

    public IReadOnlyList<MusicTrack> SavedTracks { get; private set; } = Array.Empty<MusicTrack>();
    public IReadOnlyList<string> SavedFolders { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> SavedSampleFolders { get; private set; } = Array.Empty<string>();
    public int SaveMusicCalls { get; private set; }
    public int SaveFoldersCalls { get; private set; }
    public int SaveSampleFoldersCalls { get; private set; }

    public Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_seedTracks);

    public Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
    {
        SavedTracks = tracks.ToList();
        SaveMusicCalls++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_seedFolders);

    public Task SaveScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        SavedFolders = folders.ToList();
        SaveFoldersCalls++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_seedSampleFolders);

    public Task SaveSampleFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        SavedSampleFolders = folders.ToList();
        SaveSampleFoldersCalls++;
        return Task.CompletedTask;
    }
}
