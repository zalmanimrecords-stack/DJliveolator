namespace Liveolator.Core.Library;

/// <summary>
/// Shared scan/catalog infrastructure for every media library. Subclasses declare which file
/// extensions they own and how to turn a file into an entry; this base handles incremental
/// scanning (skip unchanged, drop removed), cancellation, progress, and failure isolation
/// (one bad file never aborts the scan — it becomes a Failed entry).
/// </summary>
public abstract class MediaLibrary<TEntry> where TEntry : class, IMediaEntry
{
    private readonly IFileEnumerator _enumerator;
    private readonly Dictionary<string, TEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    // Guards _byPath: the catalog is read on the UI thread (All/TryGet) while a background re-analysis
    // pass mutates it (Upsert), so every access is serialized to stay memory-safe.
    private readonly object _gate = new();

    protected MediaLibrary(IFileEnumerator enumerator)
        => _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));

    /// <summary>File extensions this library catalogs (leading-dot, case-insensitive).</summary>
    protected abstract IReadOnlySet<string> Extensions { get; }

    /// <summary>Builds an entry for a new/changed file. Analysis-level failures should be encoded
    /// as a Failed entry; unexpected exceptions are caught by the base and routed through
    /// <see cref="CreateFailedEntry"/>.</summary>
    protected abstract Task<TEntry> CreateEntryAsync(ScannedFile file, CancellationToken cancellationToken);

    /// <summary>Builds a Failed entry when <see cref="CreateEntryAsync"/> throws.</summary>
    protected abstract TEntry CreateFailedEntry(ScannedFile file, string error);

    /// <summary>
    /// When a catalogued file changes on disk, gives a subclass the chance to <b>keep</b> the existing
    /// entry's user-authored analysis instead of rebuilding it from scratch (which would silently
    /// discard manual edits — global standard #7). Return the entry to keep, re-stamped to
    /// <paramref name="file"/> so the new fingerprint is recorded and the file is not re-flagged
    /// Modified on the next scan, or <c>null</c> to rebuild normally. Default: always rebuild.
    /// </summary>
    protected virtual TEntry? PreserveModifiedEntry(TEntry existing, ScannedFile file) => null;

    /// <summary>
    /// Returns <paramref name="entry"/> re-stamped to <paramref name="file"/> — same analysis, new
    /// file fingerprint/path. Implemented by each subclass as <c>entry with { File = file }</c>; used
    /// by <see cref="Relocate"/> to move a catalogued entry to a new path without losing its analysis.
    /// </summary>
    protected abstract TEntry WithFile(TEntry entry, ScannedFile file);

    public IReadOnlyCollection<TEntry> All { get { lock (_gate) return _byPath.Values.ToArray(); } }
    public int Count { get { lock (_gate) return _byPath.Count; } }

    public TEntry? TryGet(string path)
    {
        lock (_gate)
            return _byPath.TryGetValue(path, out TEntry? entry) ? entry : null;
    }

    /// <summary>
    /// Looks an entry up by exact path, then falls back to a file-name match. The path a deck loads can
    /// differ in form from the one the catalog was scanned under (a mapped drive S:\ vs the UNC share, or
    /// a deck-queue path), so an exact match can miss a track that IS catalogued — and the engine would
    /// then receive no analysis (BPM/grid), silently breaking beatmatch/SYNC even though the UI shows a
    /// BPM via the same fallback. The file-name fallback recovers the entry so the engine gets the BPM
    /// the UI already shows. Returns <c>null</c> when neither the exact path nor the file name matches.
    /// </summary>
    public TEntry? TryGetByPathOrName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        lock (_gate)
        {
            if (_byPath.TryGetValue(path, out TEntry? exact))
                return exact;

            string fileName = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName))
                return null;
            foreach (TEntry entry in _byPath.Values)
                if (string.Equals(System.IO.Path.GetFileName(entry.File.Path), fileName, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }
    }

    /// <summary>Thread-safely inserts or replaces a single entry — used by the background re-analysis
    /// pass to update one track's analysis without disturbing a concurrent UI read of the catalog.</summary>
    protected void Upsert(TEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
            _byPath[entry.File.Path] = entry;
    }

    /// <summary>
    /// Seeds the catalog from a previously-persisted set (the doc 13 cache) so a following
    /// <see cref="ScanAsync"/> only re-analyzes files whose size/mtime changed. Replaces any
    /// current contents; entries with duplicate paths keep the last one.
    /// </summary>
    public void Restore(IEnumerable<TEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_gate)
        {
            _byPath.Clear();
            foreach (TEntry entry in entries)
                _byPath[entry.File.Path] = entry;
        }
    }

    /// <summary>
    /// Removes a single catalogued entry by file path (the user deleted that one asset). In-memory and
    /// instant — deleting the file on disk is a separate platform concern (<see cref="IFileRemover"/>).
    /// Returns <c>true</c> if an entry was removed, <c>false</c> if no entry had that path. A following
    /// scan of the same folders re-adds the asset only if its file still exists on disk.
    /// </summary>
    public bool Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        lock (_gate)
            return _byPath.Remove(path);
    }

    /// <summary>
    /// Re-keys a catalogued entry from <paramref name="oldPath"/> to <paramref name="newFile"/>,
    /// preserving all of its analysis (the X5.2 missing-file relocation flow: a track whose original
    /// file went offline is pointed at a copy found under a new root, without re-analyzing). The entry
    /// keeps everything except its <see cref="ScannedFile"/>, which is replaced wholesale so the new
    /// path, size, and mtime are recorded and a following scan sees the file Unchanged. In-memory and
    /// instant; moving/copying the file on disk is a separate platform concern. Returns <c>true</c> if
    /// an entry at <paramref name="oldPath"/> was relocated, <c>false</c> if no entry had that path.
    /// </summary>
    public bool Relocate(string oldPath, ScannedFile newFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPath);
        if (string.IsNullOrWhiteSpace(newFile.Path))
            throw new ArgumentException("Relocated file must have a path.", nameof(newFile));

        lock (_gate)
        {
            if (!_byPath.TryGetValue(oldPath, out TEntry? existing))
                return false;

            _byPath.Remove(oldPath);
            TEntry relocated = WithFile(existing, newFile);
            _byPath[relocated.File.Path] = relocated;
            return true;
        }
    }

    /// <summary>
    /// Drops every catalogued entry whose file no longer lives under any of the given folder roots.
    /// Used when a scan folder is removed: it trims exactly the entries a re-scan of the reduced folder
    /// set would drop, but instantly and without touching disk. An entry kept by a still-retained
    /// (e.g. nested) root survives. An empty folder set clears the catalog. Returns the number removed.
    /// </summary>
    public int PruneToFolders(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        string[] roots = folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(FolderScope.Normalize)
            .ToArray();

        lock (_gate)
        {
            List<string> toRemove = _byPath.Keys
                .Where(path => !IsUnderAnyRoot(FolderScope.Normalize(path), roots))
                .ToList();
            foreach (string path in toRemove)
                _byPath.Remove(path);
            return toRemove.Count;
        }
    }

    private static bool IsUnderAnyRoot(string normalizedPath, string[] normalizedRoots)
    {
        foreach (string root in normalizedRoots)
            if (FolderScope.IsUnderNormalized(normalizedPath, root))
                return true;
        return false;
    }

    /// <summary>
    /// Scans the folders and updates the catalog incrementally: unchanged files are kept as-is
    /// (not re-processed), removed files are dropped, new/changed files are (re)built.
    /// </summary>
    /// <param name="onEntryProcessed">
    /// Invoked (and awaited) right after each new/changed entry is added to the catalog, so a caller can
    /// persist that one track the moment it is analyzed — the incremental scan. Removed and preserved
    /// entries are reported too (they changed the catalog). A handler must not throw (it should guard its
    /// own IO); an exception from it aborts the scan.
    /// </param>
    /// <param name="onEntryRemoved">
    /// Invoked (and awaited) for each file that vanished since the last scan, so the caller can drop it
    /// from persistence (a per-row store is upsert-only and won't forget it otherwise).
    /// </param>
    public async Task ScanAsync(
        IReadOnlyList<string> folders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Func<TEntry, CancellationToken, Task>? onEntryProcessed = null,
        Func<string, CancellationToken, Task>? onEntryRemoved = null)
    {
        ArgumentNullException.ThrowIfNull(folders);

        List<ScannedFile> current = _enumerator.Enumerate(folders, Extensions).ToList();
        Dictionary<string, FileFingerprint> known;
        lock (_gate)
            known = _byPath.ToDictionary(
                kv => kv.Key,
                kv => FileFingerprint.Of(kv.Value.File),
                StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<ScanDelta> deltas = IncrementalScan.Diff(current, known);

        foreach (ScanDelta delta in deltas)
            if (delta.Change == ScanChange.Removed)
            {
                lock (_gate)
                    _byPath.Remove(delta.File.Path);
                if (onEntryRemoved is not null)
                    await onEntryRemoved(delta.File.Path, cancellationToken).ConfigureAwait(false);
            }

        var toProcess = deltas
            .Where(d => d.Change is ScanChange.Added or ScanChange.Modified)
            .ToList();

        int total = toProcess.Count;
        int done = 0;
        foreach (ScanDelta delta in toProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(done, total, delta.File.Path));

            // A changed file may carry user-authored analysis we must not destroy; let the subclass
            // keep it (re-stamped to the new fingerprint) rather than rebuilding from the decoder.
            if (delta.Change == ScanChange.Modified)
            {
                TEntry? existing;
                lock (_gate)
                    _byPath.TryGetValue(delta.File.Path, out existing);
                if (existing is not null && PreserveModifiedEntry(existing, delta.File) is { } preserved)
                {
                    lock (_gate)
                        _byPath[delta.File.Path] = preserved;
                    if (onEntryProcessed is not null)
                        await onEntryProcessed(preserved, cancellationToken).ConfigureAwait(false);
                    done++;
                    continue;
                }
            }

            TEntry entry;
            try
            {
                entry = await CreateEntryAsync(delta.File, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Surface, don't crash: the failure is recorded as queryable entry state.
                entry = CreateFailedEntry(delta.File, ex.Message);
            }

            lock (_gate)
                _byPath[delta.File.Path] = entry;
            if (onEntryProcessed is not null)
                await onEntryProcessed(entry, cancellationToken).ConfigureAwait(false);
            done++;
        }

        progress?.Report(new ScanProgress(done, total, string.Empty));
    }
}
