using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Persistence;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// In-memory <see cref="IHotCueStore"/> for testing the engine's persistence wiring (A3) with no IO:
/// records what was saved per track path, serves it back on load, and can be told to throw so the
/// engine's tolerant degrade path is exercised.
/// </summary>
internal sealed class FakeHotCueStore : IHotCueStore
{
    private readonly Dictionary<string, TrackCueRecord> _byPath = new();

    /// <summary>Number of completed saves (for asserting a set/clear actually persisted).</summary>
    public int SaveCount { get; private set; }

    /// <summary>When true, load and save both throw to exercise the engine's degrade path.</summary>
    public bool Throw { get; set; }

    /// <summary>Seed a record so a subsequent Load on the engine restores it.</summary>
    public void Seed(TrackCueRecord record) => _byPath[record.TrackPath] = record;

    public Task<TrackCueRecord?> LoadAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        if (Throw)
            throw new InvalidOperationException("Simulated cue-store load failure.");
        return Task.FromResult(_byPath.TryGetValue(trackPath, out TrackCueRecord? r) ? r : null);
    }

    public Task SaveAsync(TrackCueRecord record, CancellationToken cancellationToken = default)
    {
        if (Throw)
            throw new InvalidOperationException("Simulated cue-store save failure.");
        _byPath[record.TrackPath] = record;
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string trackPath, CancellationToken cancellationToken = default)
    {
        _byPath.Remove(trackPath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> ListPathsWithCuesAsync(CancellationToken cancellationToken = default)
    {
        if (Throw)
            throw new InvalidOperationException("Simulated cue-store load failure.");
        IReadOnlyCollection<string> paths = new List<string>(_byPath.Keys);
        return Task.FromResult(paths);
    }

    /// <summary>The last-saved (or seeded) record for a path, or null when none.</summary>
    public TrackCueRecord? Get(string trackPath) => _byPath.TryGetValue(trackPath, out TrackCueRecord? r) ? r : null;
}
