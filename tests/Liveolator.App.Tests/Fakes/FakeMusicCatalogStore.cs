using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;

namespace Liveolator.App.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IMusicCatalogStore"/> for view-model tests: a faithful stand-in for the real
/// per-row store (SQLite). One backing dictionary keyed by path is the persisted catalog — whole-catalog
/// <see cref="SaveMusicAsync"/> replaces it, per-track <see cref="SaveTrackAsync"/> upserts one row, and
/// <see cref="DeleteTrackAsync"/> drops one. So an incremental scan (per-track save) and a folder removal
/// (per-path delete) round-trip exactly as they do on disk. All methods complete synchronously, so a
/// guarded fire-and-forget save settles before the test inspects it (the VM uses ImmediateScheduler).
/// </summary>
public sealed class FakeMusicCatalogStore : IMusicCatalogStore
{
    private readonly IReadOnlyList<MusicTrack> _seedTracks;
    private readonly IReadOnlyList<string> _seedFolders;
    private readonly IReadOnlyList<string> _seedSampleFolders;
    // The persisted catalog, keyed by path — every write path funnels here so SavedTracks always
    // reflects the true on-disk state regardless of which save method produced it.
    private readonly Dictionary<string, MusicTrack> _saved = new(StringComparer.OrdinalIgnoreCase);

    public FakeMusicCatalogStore(
        IEnumerable<MusicTrack>? seedTracks = null,
        IEnumerable<string>? seedFolders = null,
        IEnumerable<string>? seedSampleFolders = null)
    {
        _seedTracks = seedTracks?.ToList() ?? new List<MusicTrack>();
        _seedFolders = seedFolders?.ToList() ?? new List<string>();
        _seedSampleFolders = seedSampleFolders?.ToList() ?? new List<string>();
    }

    public IReadOnlyList<MusicTrack> SavedTracks => _saved.Values.ToList();
    public IReadOnlyList<string> SavedFolders { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> SavedSampleFolders { get; private set; } = Array.Empty<string>();
    public int SaveMusicCalls { get; private set; }
    public int SaveFoldersCalls { get; private set; }
    public int SaveSampleFoldersCalls { get; private set; }

    /// <summary>Per-track incremental saves, in call order (the incremental scan write path).</summary>
    public List<MusicTrack> SavedTrackByTrack { get; } = new();

    /// <summary>Paths deleted one at a time (removed files / pruned folders).</summary>
    public List<string> DeletedPaths { get; } = new();

    public Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_seedTracks);

    public Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
    {
        _saved.Clear();
        foreach (MusicTrack track in tracks)
            _saved[track.File.Path] = track;
        SaveMusicCalls++;
        return Task.CompletedTask;
    }

    public Task SaveTrackAsync(MusicTrack track, CancellationToken cancellationToken = default)
    {
        _saved[track.File.Path] = track;
        SavedTrackByTrack.Add(track);
        return Task.CompletedTask;
    }

    public Task DeleteTrackAsync(string path, CancellationToken cancellationToken = default)
    {
        _saved.Remove(path);
        DeletedPaths.Add(path);
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
