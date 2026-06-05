using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of the persisted music catalog (the doc 13 / doc 16 cache).</summary>
public sealed record MusicCatalogSnapshot(int Version, IReadOnlyList<MusicTrack> Tracks)
{
    // v2 (2026-06-05): MusicTrack gained TrackMetadata (tags). An older snapshot is missing it,
    // so it is discarded on load and re-scanned rather than served with empty metadata.
    public const int CurrentVersion = 2;
}

/// <summary>Versioned on-disk shape of the persisted visual-media catalog (doc 13).</summary>
public sealed record VisualCatalogSnapshot(int Version, IReadOnlyList<VisualAsset> Assets)
{
    public const int CurrentVersion = 1;
}

/// <summary>Versioned on-disk shape of the persisted scan-folder roots (doc 13).</summary>
public sealed record ScanFoldersSnapshot(int Version, IReadOnlyList<string> Folders)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists the analyzed music catalog as JSON under the per-user app-data root, so a later run
/// re-loads it and only re-analyzes new/changed files. A missing or corrupt cache yields an
/// empty catalog (triggering a fresh scan) and reports a warning — it never crashes the app
/// (global standards #16, #26).
/// </summary>
public sealed class JsonCatalogStore : IMusicCatalogStore
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

    /// <summary>Full path of the persisted scan-folder-roots JSON file.</summary>
    public string ScanFoldersPath => Path.Combine(_directory, "scan-folders.json");

    /// <summary>Default persistence root: <c>%APPDATA%/Liveolator</c> (or the XDG/Mac equivalent).</summary>
    public static string DefaultRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liveolator");

    public Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        return SaveAsync(MusicCatalogPath, new MusicCatalogSnapshot(MusicCatalogSnapshot.CurrentVersion, tracks.ToList()), cancellationToken);
    }

    /// <summary>
    /// Loads the persisted music catalog, or an empty list when none exists, it is unreadable, or it
    /// was written by an older schema version (a version mismatch triggers a clean re-scan).
    /// </summary>
    public async Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
    {
        MusicCatalogSnapshot? snapshot =
            await LoadAsync<MusicCatalogSnapshot>(MusicCatalogPath, cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
            return Array.Empty<MusicTrack>();

        if (snapshot.Version != MusicCatalogSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Catalog cache at '{MusicCatalogPath}' is version {snapshot.Version} " +
                $"(expected {MusicCatalogSnapshot.CurrentVersion}); re-analyzing from scratch.");
            return Array.Empty<MusicTrack>();
        }

        return snapshot.Tracks;
    }

    public Task SaveVisualAsync(IEnumerable<VisualAsset> assets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        return SaveAsync(VisualCatalogPath, new VisualCatalogSnapshot(VisualCatalogSnapshot.CurrentVersion, assets.ToList()), cancellationToken);
    }

    /// <summary>Loads the persisted visual catalog, or an empty list when none exists or it is unreadable.</summary>
    public async Task<IReadOnlyList<VisualAsset>> LoadVisualAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync<VisualCatalogSnapshot>(VisualCatalogPath, cancellationToken).ConfigureAwait(false))?.Assets
           ?? Array.Empty<VisualAsset>();

    public Task SaveScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return SaveAsync(ScanFoldersPath, new ScanFoldersSnapshot(ScanFoldersSnapshot.CurrentVersion, folders.ToList()), cancellationToken);
    }

    /// <summary>
    /// Loads the persisted scan-folder roots, or an empty list when none exist, the file is unreadable,
    /// or it was written by an incompatible schema version (mirrors the music-catalog load policy).
    /// </summary>
    public async Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken cancellationToken = default)
    {
        ScanFoldersSnapshot? snapshot =
            await LoadAsync<ScanFoldersSnapshot>(ScanFoldersPath, cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
            return Array.Empty<string>();

        if (snapshot.Version != ScanFoldersSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Scan-folders file at '{ScanFoldersPath}' is version {snapshot.Version} " +
                $"(expected {ScanFoldersSnapshot.CurrentVersion}); ignoring.");
            return Array.Empty<string>();
        }

        return snapshot.Folders ?? Array.Empty<string>();
    }

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
