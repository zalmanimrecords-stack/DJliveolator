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

    public IReadOnlyCollection<TEntry> All => _byPath.Values.ToArray();
    public int Count => _byPath.Count;
    public TEntry? TryGet(string path) => _byPath.TryGetValue(path, out TEntry? entry) ? entry : null;

    /// <summary>
    /// Scans the folders and updates the catalog incrementally: unchanged files are kept as-is
    /// (not re-processed), removed files are dropped, new/changed files are (re)built.
    /// </summary>
    public async Task ScanAsync(
        IReadOnlyList<string> folders,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);

        List<ScannedFile> current = _enumerator.Enumerate(folders, Extensions).ToList();
        var known = _byPath.ToDictionary(
            kv => kv.Key,
            kv => FileFingerprint.Of(kv.Value.File),
            StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<ScanDelta> deltas = IncrementalScan.Diff(current, known);

        foreach (ScanDelta delta in deltas)
            if (delta.Change == ScanChange.Removed)
                _byPath.Remove(delta.File.Path);

        var toProcess = deltas
            .Where(d => d.Change is ScanChange.Added or ScanChange.Modified)
            .ToList();

        int total = toProcess.Count;
        int done = 0;
        foreach (ScanDelta delta in toProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(done, total, delta.File.Path));

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

            _byPath[delta.File.Path] = entry;
            done++;
        }

        progress?.Report(new ScanProgress(done, total, string.Empty));
    }
}
