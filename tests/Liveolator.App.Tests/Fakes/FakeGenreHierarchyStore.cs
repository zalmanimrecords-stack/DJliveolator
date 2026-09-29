using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;

namespace Liveolator.App.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IGenreHierarchyStore"/> for view-model tests: <see cref="LoadAsync"/> returns a
/// seeded hierarchy synchronously (<see cref="Task.FromResult{TResult}"/>), so the view-model's
/// construction-time load resolves inline under the test's <c>ImmediateScheduler</c> — no extra test
/// synchronization needed, same pattern as <see cref="FakeHotCueStore"/>.
/// </summary>
public sealed class FakeGenreHierarchyStore : IGenreHierarchyStore
{
    private GenreHierarchy _hierarchy;

    public FakeGenreHierarchyStore(GenreHierarchy? seed = null) => _hierarchy = seed ?? new GenreHierarchy();

    /// <summary>How many times <see cref="SaveAsync"/> was called (Phase 3: proves a "Group under…" edit
    /// actually persisted, not just the in-memory hierarchy).</summary>
    public int SaveCount { get; private set; }

    /// <summary>The hierarchy passed to the most recent <see cref="SaveAsync"/> call, or null if none yet.</summary>
    public GenreHierarchy? LastSaved { get; private set; }

    public Task<GenreHierarchy> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_hierarchy);

    public Task SaveAsync(GenreHierarchy hierarchy, CancellationToken cancellationToken = default)
    {
        _hierarchy = hierarchy;
        LastSaved = hierarchy;
        SaveCount++;
        return Task.CompletedTask;
    }
}
