using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of the persisted music catalog (the doc 13 / doc 16 cache).</summary>
public sealed record MusicCatalogSnapshot(int Version, IReadOnlyList<MusicTrack> Tracks)
{
    public const int CurrentVersion = 1;
}

/// <summary>Versioned on-disk shape of the persisted visual-media catalog (doc 13).</summary>
public sealed record VisualCatalogSnapshot(int Version, IReadOnlyList<VisualAsset> Assets)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists the analyzed music catalog as JSON under the per-user app-data root, so a later run
/// re-loads it and only re-analyzes new/changed files. A missing or corrupt cache yields an
/// empty catalog (triggering a fresh scan) and reports a warning — it never crashes the app
/// (global standards #16, #26).
/// </summary>
public sealed class JsonCatalogStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;
    private readonly Action<string>? _onWarning;

    public JsonCatalogStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _directory = rootDirectory ?? DefaultRoot();
        _onWarning = onWarning;
    }

    /// <summary>Full path of the music-catalog JSON file.</summary>
    public string MusicCatalogPath => Path.Combine(_directory, "catalog.music.json");

    /// <summary>Full path of the visual-media-catalog JSON file.</summary>
    public string VisualCatalogPath => Path.Combine(_directory, "catalog.visual.json");

    /// <summary>Default persistence root: <c>%APPDATA%/Liveolator</c> (or the XDG/Mac equivalent).</summary>
    public static string DefaultRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liveolator");

    public Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        return SaveAsync(MusicCatalogPath, new MusicCatalogSnapshot(MusicCatalogSnapshot.CurrentVersion, tracks.ToList()), cancellationToken);
    }

    /// <summary>Loads the persisted music catalog, or an empty list when none exists or it is unreadable.</summary>
    public async Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync<MusicCatalogSnapshot>(MusicCatalogPath, cancellationToken).ConfigureAwait(false))?.Tracks
           ?? Array.Empty<MusicTrack>();

    public Task SaveVisualAsync(IEnumerable<VisualAsset> assets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        return SaveAsync(VisualCatalogPath, new VisualCatalogSnapshot(VisualCatalogSnapshot.CurrentVersion, assets.ToList()), cancellationToken);
    }

    /// <summary>Loads the persisted visual catalog, or an empty list when none exists or it is unreadable.</summary>
    public async Task<IReadOnlyList<VisualAsset>> LoadVisualAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync<VisualCatalogSnapshot>(VisualCatalogPath, cancellationToken).ConfigureAwait(false))?.Assets
           ?? Array.Empty<VisualAsset>();

    private async Task SaveAsync<T>(string path, T snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        // Write to a temp file then move, so an interrupted write never corrupts the live cache.
        string tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    private async Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Catalog cache at '{path}' is unreadable ({ex.Message}); re-analyzing from scratch.");
            return null;
        }
    }
}
