using System.Text.Json;
using Liveolator.Core.Persistence;
using CorePlaylist = Liveolator.Core.Playlist.Playlist;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of a saved playlist (the doc 13 <c>live/playlists/</c> layout).</summary>
public sealed record PlaylistSnapshot(int Version, string Name, IReadOnlyList<string> TrackPaths)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists named playlists as one JSON file per playlist under
/// <c>&lt;root&gt;/live/playlists/&lt;sanitized-name&gt;.json</c> (the doc 13 layout). Mirrors
/// <see cref="JsonCatalogStore"/>: tolerant loads (missing / unreadable / incompatible-version →
/// <c>null</c> + warning, never a throw) and atomic temp-then-move saves so an interrupted write
/// never corrupts a saved set (global standards #16/#26, #20/#22).
/// </summary>
public sealed class JsonPlaylistStore : IPlaylistStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly Action<string>? _onWarning;

    public JsonPlaylistStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _directory = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "live", "playlists");
        _onWarning = onWarning;
    }

    /// <summary>The directory holding the per-playlist JSON files.</summary>
    public string Directory => _directory;

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(_directory))
            return Array.Empty<string>();

        var names = new List<string>();
        foreach (string file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlaylistSnapshot? snapshot = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && snapshot.Version == PlaylistSnapshot.CurrentVersion)
                names.Add(snapshot.Name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public async Task<CorePlaylist?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        PlaylistSnapshot? snapshot = await ReadAsync(PathFor(name), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        if (snapshot.Version != PlaylistSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Playlist '{name}' is version {snapshot.Version} (expected {PlaylistSnapshot.CurrentVersion}); ignoring.");
            return null;
        }

        return new CorePlaylist(snapshot.Name, snapshot.TrackPaths);
    }

    public async Task SaveAsync(CorePlaylist playlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentException.ThrowIfNullOrWhiteSpace(playlist.Name);

        System.IO.Directory.CreateDirectory(_directory);
        string path = PathFor(playlist.Name);
        string tempPath = path + ".tmp";
        var snapshot = new PlaylistSnapshot(PlaylistSnapshot.CurrentVersion, playlist.Name, playlist.TrackPaths.ToList());

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string name) => Path.Combine(_directory, Sanitize(name) + ".json");

    // Map a display name to a safe filename: strip characters illegal in a filename. The real display
    // name is stored inside the JSON, so two names that sanitize alike simply share a slot (last save wins).
    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        cleaned = cleaned.Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "playlist" : cleaned;
    }

    private async Task<PlaylistSnapshot?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return await JsonSerializer.DeserializeAsync<PlaylistSnapshot>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Playlist file at '{path}' is unreadable ({ex.Message}); skipping.");
            return null;
        }
    }
}
