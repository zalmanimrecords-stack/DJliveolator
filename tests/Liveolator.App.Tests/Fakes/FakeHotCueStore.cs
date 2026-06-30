using Liveolator.Core.Persistence;

namespace Liveolator.App.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IHotCueStore"/> for view-model tests with no IO: serves seeded cue records back
/// by track path and can be told to throw so a load-failure degrade path is exercised.
/// </summary>
public sealed class FakeHotCueStore : IHotCueStore
{
    private readonly Dictionary<string, TrackCueRecord> _byPath = new();

    /// <summary>When true, <see cref="LoadAsync"/> throws to exercise the caller's degrade path.</summary>
    public bool ThrowOnLoad { get; set; }

    /// <summary>Seed a record so a subsequent load returns it.</summary>
    public void Seed(TrackCueRecord record) => _byPath[record.TrackPath] = record;

    public Task<TrackCueRecord?> LoadAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        if (ThrowOnLoad)
            throw new InvalidOperationException("Simulated cue-store load failure.");
        return Task.FromResult(_byPath.TryGetValue(trackPath, out TrackCueRecord? r) ? r : null);
    }

    public Task SaveAsync(TrackCueRecord record, CancellationToken cancellationToken = default)
    {
        _byPath[record.TrackPath] = record;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        _byPath.Remove(trackPath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> ListPathsWithCuesAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnLoad)
            throw new InvalidOperationException("Simulated cue-store load failure.");
        IReadOnlyCollection<string> paths = _byPath
            .Where(kv => kv.Value.HotCues.Count > 0).Select(kv => kv.Key).ToList();
        return Task.FromResult(paths);
    }

    /// <summary>The last-saved (or seeded) record for a path, or null when none — for test assertions.</summary>
    public TrackCueRecord? Get(string trackPath) => _byPath.TryGetValue(trackPath, out TrackCueRecord? r) ? r : null;
}
