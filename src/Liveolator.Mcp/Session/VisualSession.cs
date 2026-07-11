using System.Diagnostics;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
using Liveolator.Mcp.Contracts;
using Microsoft.Extensions.Logging;

namespace Liveolator.Mcp.Session;

/// <summary>
/// Owns the visual-media catalog (images + video clips) for the MCP server: loads the persisted
/// cache once, scans folders (probing dimensions/duration), persists, and answers queries.
/// Access is serialized because <see cref="VisualMediaLibrary"/> is not thread-safe. Mirrors
/// <see cref="LibrarySession"/> for the visual domain.
/// </summary>
public sealed class VisualSession
{
    private readonly VisualMediaLibrary _library;
    private readonly IVisualCatalogStore _store;
    private readonly ILogger<VisualSession> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SortedSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public VisualSession(
        IFileEnumerator enumerator,
        IVisualMediaProbe probe,
        IVisualCatalogStore store,
        ILogger<VisualSession> logger)
    {
        _library = new VisualMediaLibrary(enumerator, probe);
        _store = store;
        _logger = logger;
    }

    /// <summary>Scans the given folders for images/videos, persists the catalog, and returns a summary.</summary>
    public async Task<VisualScanSummary> ScanAsync(IReadOnlyList<string> folders, bool force, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            foreach (string folder in folders)
            {
                string? canonical = Canonicalize(folder);
                if (canonical is not null)
                    _folders.Add(canonical);
            }
            if (_folders.Count == 0)
                throw new ArgumentException("No folders to scan. Pass at least one folder path.", nameof(folders));

            if (force)
                _library.Restore(Array.Empty<VisualAsset>());

            var processed = 0;
            var progress = new Progress<ScanProgress>(p => processed = Math.Max(processed, p.Total));

            var stopwatch = Stopwatch.StartNew();
            await _library.ScanAsync(_folders.ToList(), progress, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            await _store.SaveVisualAsync(_library.All, cancellationToken).ConfigureAwait(false);

            return BuildSummary(processed, stopwatch.ElapsedMilliseconds);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<VisualAsset>> SnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _library.All.ToList();
        }
        finally { _gate.Release(); }
    }

    public async Task<VisualAsset?> GetAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _library.TryGet(path);
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return;

        IReadOnlyList<VisualAsset> cached = await _store.LoadVisualAsync(cancellationToken).ConfigureAwait(false);
        if (cached.Count > 0)
        {
            _library.Restore(cached);
            foreach (string folder in cached
                         .Select(a => Canonicalize(Path.GetDirectoryName(a.File.Path)))
                         .Where(d => d is not null)
                         .Select(d => d!)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                _folders.Add(folder);
            _logger.LogInformation("Loaded {Count} visual assets from the catalog cache.", cached.Count);
        }
        _loaded = true;
    }

    private static string? Canonicalize(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        try
        {
            return Path.GetFullPath(folder.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private VisualScanSummary BuildSummary(int processed, long elapsedMs)
    {
        IReadOnlyCollection<VisualAsset> all = _library.All;
        var failures = all
            .Where(a => a.Status == MediaAnalysisStatus.Failed)
            .Select(a => new FailedTrack(a.File.Path, a.Error ?? "unknown error"))
            .Take(50)
            .ToList();

        return new VisualScanSummary(
            TotalAssets: all.Count,
            Images: all.Count(a => a.Kind == VisualMediaKind.Image),
            Videos: all.Count(a => a.Kind == VisualMediaKind.Video),
            Failed: all.Count(a => a.Status == MediaAnalysisStatus.Failed),
            ProcessedThisScan: processed,
            ElapsedMs: elapsedMs,
            Folders: _folders.ToList(),
            Failures: failures);
    }
}
