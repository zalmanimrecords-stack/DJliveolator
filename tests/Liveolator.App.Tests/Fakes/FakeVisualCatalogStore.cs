using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;

namespace Liveolator.App.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IVisualCatalogStore"/> for view-model tests: records what was saved and
/// replays a seeded set on load, so persistence round-trips can be asserted with no disk access.
/// All methods complete synchronously, so a guarded fire-and-forget save settles before the test
/// inspects it (the VM uses <see cref="System.Reactive.Concurrency.ImmediateScheduler"/> in tests).
/// </summary>
public sealed class FakeVisualCatalogStore : IVisualCatalogStore
{
    private readonly IReadOnlyList<VisualAsset> _seedAssets;
    private readonly IReadOnlyList<string> _seedFolders;

    public FakeVisualCatalogStore(
        IEnumerable<VisualAsset>? seedAssets = null,
        IEnumerable<string>? seedFolders = null)
    {
        _seedAssets = seedAssets?.ToList() ?? new List<VisualAsset>();
        _seedFolders = seedFolders?.ToList() ?? new List<string>();
    }

    public IReadOnlyList<VisualAsset> SavedAssets { get; private set; } = Array.Empty<VisualAsset>();
    public IReadOnlyList<string> SavedFolders { get; private set; } = Array.Empty<string>();
    public int SaveAssetsCalls { get; private set; }
    public int SaveFoldersCalls { get; private set; }

    public Task<IReadOnlyList<VisualAsset>> LoadVisualAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_seedAssets);

    public Task SaveVisualAsync(IEnumerable<VisualAsset> assets, CancellationToken cancellationToken = default)
    {
        SavedAssets = assets.ToList();
        SaveAssetsCalls++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> LoadVisualScanFoldersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_seedFolders);

    public Task SaveVisualScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        SavedFolders = folders.ToList();
        SaveFoldersCalls++;
        return Task.CompletedTask;
    }
}
