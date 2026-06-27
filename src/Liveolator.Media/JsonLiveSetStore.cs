using Liveolator.Core.Persistence;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of the live set (the doc 13 <c>live/</c> layout).</summary>
public sealed record LiveSetSnapshot(int Version, IReadOnlyList<string> TrackPaths)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists one live DJ set as one JSON file under <c>&lt;root&gt;/live/</c> (the doc 13 layout) —
/// <c>current-set.json</c> by default (deck A's set); a second instance with another file name holds
/// deck B's queue. Mirrors <see cref="JsonPlaylistStore"/>: tolerant loads (missing / unreadable /
/// incompatible-version → <c>null</c> + warning, never a throw) and atomic temp-then-move saves so an
/// interrupted write never corrupts the set (global standards #16/#26, #20/#22).
/// </summary>
public sealed class JsonLiveSetStore : ILiveSetStore
{
    private const string DefaultFileName = "current-set.json";

    private readonly string _path;
    private readonly Action<string>? _onWarning;
    private readonly JsonFileSnapshotIo _io;

    public JsonLiveSetStore(
        string? rootDirectory = null, Action<string>? onWarning = null, string fileName = DefaultFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        _path = System.IO.Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "live", fileName);
        _onWarning = onWarning;
        _io = new JsonFileSnapshotIo(onWarning);
    }

    /// <summary>The full path of the JSON file holding the set.</summary>
    public string Path => _path;

    public async Task<IReadOnlyList<string>?> LoadAsync(CancellationToken cancellationToken = default)
    {
        LiveSetSnapshot? snapshot = await _io.LoadAsync<LiveSetSnapshot>(_path, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        if (snapshot.Version != LiveSetSnapshot.CurrentVersion)
        {
            _io.WarnVersionMismatch(_path, snapshot.Version, LiveSetSnapshot.CurrentVersion);
            return null;
        }

        return snapshot.TrackPaths;
    }

    public Task SaveAsync(IReadOnlyList<string> trackPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackPaths);

        // Atomic temp-then-move via the shared helper (unique temp name + SemaphoreSlim gate), so the
        // fire-and-forget autosave on every queue edit can't race two writes onto one temp path (doc 31 H1).
        var snapshot = new LiveSetSnapshot(LiveSetSnapshot.CurrentVersion, trackPaths.ToList());
        return _io.SaveAsync(_path, snapshot, cancellationToken);
    }
}
