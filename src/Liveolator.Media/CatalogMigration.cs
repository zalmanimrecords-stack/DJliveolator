using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;

namespace Liveolator.Media;

/// <summary>
/// One-time upgrade of a persisted catalog from the legacy whole-file <see cref="JsonCatalogStore"/> to
/// the per-row <see cref="SqliteCatalogStore"/>. Runs at startup before the app/MCP reads the catalog: if
/// the SQLite database does not exist yet but a JSON catalog does, the JSON contents are copied into
/// SQLite so users don't re-scan their whole (network) library on first launch after the switch.
/// Idempotent — a present database (or a fresh install with no JSON) is a no-op. Guarded: a migration
/// failure degrades to an empty database + a warning, never a crash (global standards #16/#26).
/// </summary>
public static class CatalogMigration
{
    /// <summary>
    /// Migrates the JSON catalog under <paramref name="rootDirectory"/> into a SQLite database in the
    /// same directory, when one is not already present. Safe to call from both the app and the MCP server
    /// (SQLite's WAL + busy_timeout serialize a concurrent second writer, and every write is an upsert).
    /// </summary>
    public static void JsonToSqliteIfNeeded(string rootDirectory, Action<string>? onWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var json = new JsonCatalogStore(rootDirectory, onWarning);
        using var sqlite = new SqliteCatalogStore(rootDirectory, onWarning);

        // A present database means we've already migrated (or started fresh on SQLite) — nothing to do.
        if (File.Exists(sqlite.DatabasePath))
            return;
        // A fresh install with no legacy JSON has nothing to carry over.
        if (!File.Exists(json.MusicCatalogPath) && !File.Exists(json.ScanFoldersPath)
            && !File.Exists(json.VisualCatalogPath))
            return;

        try
        {
            IReadOnlyList<MusicTrack> tracks = json.LoadMusicAsync().GetAwaiter().GetResult();
            if (tracks.Count > 0)
                sqlite.SaveMusicAsync(tracks).GetAwaiter().GetResult();

            IReadOnlyList<VisualAsset> visuals = json.LoadVisualAsync().GetAwaiter().GetResult();
            if (visuals.Count > 0)
                sqlite.SaveVisualAsync(visuals).GetAwaiter().GetResult();

            IReadOnlyList<string> scanFolders = json.LoadScanFoldersAsync().GetAwaiter().GetResult();
            if (scanFolders.Count > 0)
                sqlite.SaveScanFoldersAsync(scanFolders).GetAwaiter().GetResult();

            IReadOnlyList<string> visualScanFolders = json.LoadVisualScanFoldersAsync().GetAwaiter().GetResult();
            if (visualScanFolders.Count > 0)
                sqlite.SaveVisualScanFoldersAsync(visualScanFolders).GetAwaiter().GetResult();

            IReadOnlyList<string> sampleFolders = json.LoadSampleFoldersAsync().GetAwaiter().GetResult();
            if (sampleFolders.Count > 0)
                sqlite.SaveSampleFoldersAsync(sampleFolders).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            onWarning?.Invoke($"Catalog migration JSON->SQLite failed ({ex.Message}); starting from an empty database.");
        }
    }
}
