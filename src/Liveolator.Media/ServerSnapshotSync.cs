using Liveolator.Core;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

/// <summary>What a sync did: the folders the server now manages, and one status line per folder.</summary>
public sealed record ServerSnapshotSyncResult(IReadOnlyList<string> ManagedRoots, IReadOnlyList<string> Messages)
{
    public static ServerSnapshotSyncResult None { get; } = new(Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>
/// Adopts the analysis a music host published for a scan folder, so this machine does not decode that
/// folder over the network. The host scans its own disk and writes a verified snapshot to
/// <c>&lt;folder&gt;/.liveolator/catalog.db</c>, recording in it the path it scanned the folder under.
/// </summary>
/// <remarks>
/// A folder with a readable snapshot is "server-managed": the caller leaves it out of its own scan. The
/// merge is <see cref="ServerCatalogPull"/> with adoption, so a local tempo and a hand correction still
/// win. A row the server no longer lists is dropped only when its file is gone too, because a file the
/// server has not reached yet would otherwise vanish from the library until the next publish.
/// </remarks>
public sealed class ServerSnapshotSync
{
    public const string SnapshotFolder = ".liveolator";

    private readonly MusicLibrary _library;
    private readonly IMusicCatalogStore _store;
    private readonly Func<string, bool> _fileExists;
    private readonly Action<string>? _onWarning;

    public ServerSnapshotSync(
        MusicLibrary library, IMusicCatalogStore store, Func<string, bool>? fileExists = null,
        Action<string>? onWarning = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _fileExists = fileExists ?? File.Exists;
        _onWarning = onWarning;
    }

    public async Task<ServerSnapshotSyncResult> SyncAsync(
        IReadOnlyList<string> roots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var managed = new List<string>();
        var messages = new List<string>();

        foreach (string root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            string snapshot = Path.Combine(root, SnapshotFolder, "catalog.db");
            if (!File.Exists(snapshot))
                continue;

            (IReadOnlyList<MusicTrack> Tracks, string ServerRoot)? published =
                await LoadAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (published is not { } server)
                continue;

            ServerCatalogPullPlan plan = ServerCatalogPull.Plan(
                server.Tracks, _library.All, server.ServerRoot, root, adoptMissing: true);

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MusicTrack track in server.Tracks)
                if (PortablePath.Rebase(track.File.Path, server.ServerRoot, root) is { } local)
                    listed.Add(local);
            List<string> gone = _library.All
                .Select(t => t.File.Path)
                .Where(p => FolderScope.IsUnder(p, root) && !listed.Contains(p) && !_fileExists(p))
                .ToList();

            var goneSet = new HashSet<string>(gone, StringComparer.OrdinalIgnoreCase);
            _library.Restore(ServerCatalogPull.Apply(_library.All, plan).Where(t => !goneSet.Contains(t.File.Path)));
            if (plan.TracksToUpsert.Count > 0)
                await _store.SaveMusicAsync(plan.TracksToUpsert, cancellationToken).ConfigureAwait(false);
            foreach (string path in gone)
                await _store.DeleteTrackAsync(path, cancellationToken).ConfigureAwait(false);

            managed.Add(root);
            messages.Add(gone.Count == 0
                ? $"{root}: {plan.Describe()}"
                : $"{root}: {plan.Describe()} {gone.Count} removed track(s) dropped.");
        }

        return new ServerSnapshotSyncResult(managed, messages);
    }

    // Read through a verified local copy: the snapshot sits on a share, and SQLite must never open it
    // in place (no WAL over SMB, no writes next to another machine's file).
    private async Task<(IReadOnlyList<MusicTrack>, string)?> LoadAsync(string snapshot, CancellationToken cancellationToken)
    {
        string copyDirectory = Path.Combine(Path.GetTempPath(), $"liveolator-server-snapshot-{Guid.NewGuid():N}");
        try
        {
            SqliteCatalogSnapshot.Copy(snapshot, Path.Combine(copyDirectory, "catalog.db"), sourceIsForeign: true);
            using var copy = new SqliteCatalogStore(copyDirectory, _onWarning);
            IReadOnlyList<string> scanned = await copy.LoadScanFoldersAsync(cancellationToken).ConfigureAwait(false);
            if (scanned.Count != 1)
            {
                _onWarning?.Invoke(
                    $"Server snapshot '{snapshot}' names {scanned.Count} scan folder(s), not one; the folder is scanned locally instead.");
                return null;
            }
            return (await copy.LoadMusicAsync(cancellationToken).ConfigureAwait(false), scanned[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            _onWarning?.Invoke($"Server snapshot '{snapshot}' could not be read ({ex.Message}); the folder is scanned locally instead.");
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(copyDirectory))
                    Directory.Delete(copyDirectory, recursive: true);
            }
            catch (IOException ex)
            {
                _onWarning?.Invoke($"Could not remove the snapshot copy '{copyDirectory}' ({ex.Message}).");
            }
        }
    }
}
