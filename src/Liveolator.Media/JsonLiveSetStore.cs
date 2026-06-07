using System.Text.Json;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of the live set (the doc 13 <c>live/</c> layout).</summary>
public sealed record LiveSetSnapshot(int Version, IReadOnlyList<string> TrackPaths)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists the single live DJ set as one JSON file at <c>&lt;root&gt;/live/current-set.json</c>
/// (the doc 13 layout). Mirrors <see cref="JsonPlaylistStore"/>: tolerant loads (missing / unreadable /
/// incompatible-version → <c>null</c> + warning, never a throw) and atomic temp-then-move saves so an
/// interrupted write never corrupts the set (global standards #16/#26, #20/#22).
/// </summary>
public sealed class JsonLiveSetStore : ILiveSetStore
{
    private const string FileName = "current-set.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Action<string>? _onWarning;

    public JsonLiveSetStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _path = System.IO.Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "live", FileName);
        _onWarning = onWarning;
    }

    /// <summary>The full path of the JSON file holding the set.</summary>
    public string Path => _path;

    public async Task<IReadOnlyList<string>?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return null;

        LiveSetSnapshot? snapshot;
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read);
            snapshot = await JsonSerializer.DeserializeAsync<LiveSetSnapshot>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Live set file at '{_path}' is unreadable ({ex.Message}); ignoring.");
            return null;
        }

        if (snapshot is null)
            return null;

        if (snapshot.Version != LiveSetSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Live set is version {snapshot.Version} (expected {LiveSetSnapshot.CurrentVersion}); ignoring.");
            return null;
        }

        return snapshot.TrackPaths;
    }

    public async Task SaveAsync(IReadOnlyList<string> trackPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackPaths);

        string directory = System.IO.Path.GetDirectoryName(_path)!;
        System.IO.Directory.CreateDirectory(directory);
        string tempPath = _path + ".tmp";
        var snapshot = new LiveSetSnapshot(LiveSetSnapshot.CurrentVersion, trackPaths.ToList());

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _path, overwrite: true);
    }
}
