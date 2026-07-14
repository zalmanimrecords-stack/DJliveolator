using System.Text.Json;
using System.Text.Json.Serialization;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of the persisted music catalog (the doc 13 / doc 16 cache).</summary>
public sealed record MusicCatalogSnapshot(int Version, IReadOnlyList<MusicTrack> Tracks)
{
    // v2 (2026-06-05): MusicTrack gained TrackMetadata (tags).
    // v3 (2026-06-06): MusicTrack gained Kind (Track/Sample). An older snapshot lacks the
    // classification, so it is discarded on load and re-scanned rather than served miscategorized.
    public const int CurrentVersion = 3;
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
public sealed class JsonCatalogStore : IMusicCatalogStore, IVisualCatalogStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;
    private readonly Action<string>? _onWarning;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

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

    /// <summary>Full path of the persisted visual scan-folder-roots JSON file (kept separate from music).</summary>
    public string VisualScanFoldersPath => Path.Combine(_directory, "scan-folders.visual.json");

    /// <summary>Full path of the persisted sample-folder designations JSON file.</summary>
    public string SampleFoldersPath => Path.Combine(_directory, "sample-folders.json");

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

    // The JSON store keeps no in-memory catalog, so a per-track write is read-merge-write of the whole
    // file — correct but O(catalog). It exists only so this legacy store still satisfies the seam; the
    // app wires the per-row SqliteCatalogStore for the incremental scan, where these are O(1).
    public async Task SaveTrackAsync(MusicTrack track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        List<MusicTrack> tracks = (await LoadMusicAsync(cancellationToken).ConfigureAwait(false)).ToList();
        int i = tracks.FindIndex(t => string.Equals(t.File.Path, track.File.Path, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
            tracks[i] = track;
        else
            tracks.Add(track);
        await SaveMusicAsync(tracks, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteTrackAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        List<MusicTrack> tracks = (await LoadMusicAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (tracks.RemoveAll(t => string.Equals(t.File.Path, path, StringComparison.OrdinalIgnoreCase)) > 0)
            await SaveMusicAsync(tracks, cancellationToken).ConfigureAwait(false);
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

    public Task SaveVisualScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return SaveAsync(VisualScanFoldersPath, new ScanFoldersSnapshot(ScanFoldersSnapshot.CurrentVersion, folders.ToList()), cancellationToken);
    }

    /// <summary>
    /// Loads the persisted visual scan-folder roots, or an empty list when none exist, the file is
    /// unreadable, or it was written by an incompatible schema version (mirrors the music-scan policy).
    /// </summary>
    public async Task<IReadOnlyList<string>> LoadVisualScanFoldersAsync(CancellationToken cancellationToken = default)
    {
        ScanFoldersSnapshot? snapshot =
            await LoadAsync<ScanFoldersSnapshot>(VisualScanFoldersPath, cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
            return Array.Empty<string>();

        if (snapshot.Version != ScanFoldersSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Visual scan-folders file at '{VisualScanFoldersPath}' is version {snapshot.Version} " +
                $"(expected {ScanFoldersSnapshot.CurrentVersion}); ignoring.");
            return Array.Empty<string>();
        }

        return snapshot.Folders ?? Array.Empty<string>();
    }

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

    public Task SaveSampleFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return SaveAsync(SampleFoldersPath, new ScanFoldersSnapshot(ScanFoldersSnapshot.CurrentVersion, folders.ToList()), cancellationToken);
    }

    /// <summary>
    /// Loads the folders the user designated as "samples", or an empty list when none exist, the file is
    /// unreadable, or it was written by an incompatible schema version (mirrors the scan-folders policy).
    /// </summary>
    public async Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken cancellationToken = default)
    {
        ScanFoldersSnapshot? snapshot =
            await LoadAsync<ScanFoldersSnapshot>(SampleFoldersPath, cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
            return Array.Empty<string>();

        if (snapshot.Version != ScanFoldersSnapshot.CurrentVersion)
        {
            _onWarning?.Invoke(
                $"Sample-folders file at '{SampleFoldersPath}' is version {snapshot.Version} " +
                $"(expected {ScanFoldersSnapshot.CurrentVersion}); ignoring.");
            return Array.Empty<string>();
        }

        return snapshot.Folders ?? Array.Empty<string>();
    }

    private async Task SaveAsync<T>(string path, T snapshot, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_directory);
            // A unique temp file also keeps abandoned writes from colliding after cancellation.
            tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
            tempPath = null;
        }
        finally
        {
            if (tempPath is not null)
                File.Delete(tempPath);
            _saveGate.Release();
        }
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
