using System.Diagnostics;
using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Media;
using Liveolator.Mcp.Contracts;
using Microsoft.Extensions.Logging;

namespace Liveolator.Mcp.Session;

/// <summary>
/// Owns the live music catalog for the MCP server: loads the persisted cache once, runs scans,
/// persists results, and answers queries. Access is serialized with a semaphore because the
/// underlying <see cref="MusicLibrary"/> is not thread-safe and tool calls may overlap.
/// </summary>
public sealed class LibrarySession
{
    private readonly MusicLibrary _library;
    private readonly JsonCatalogStore _store;
    private readonly ILogger<LibrarySession> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SortedSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public LibrarySession(
        IFileEnumerator enumerator,
        IAudioDecoder decoder,
        TrackAnalyzer analyzer,
        ITrackMetadataReader metadataReader,
        JsonCatalogStore store,
        ILogger<LibrarySession> logger)
    {
        _library = new MusicLibrary(enumerator, decoder, analyzer, metadataReader);
        _store = store;
        _logger = logger;
    }

    /// <summary>Scans (and analyzes) the given folders, persists the catalog, and returns a summary.
    /// Always re-scans the full accumulated folder set so previously-catalogued folders are kept.</summary>
    public async Task<ScanSummary> ScanAsync(IReadOnlyList<string> folders, bool force, CancellationToken cancellationToken)
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
                _library.Restore(Array.Empty<MusicTrack>()); // drop cache so every file re-analyzes

            var processed = 0;
            var progress = new Progress<ScanProgress>(p => processed = Math.Max(processed, p.Total));

            var stopwatch = Stopwatch.StartNew();
            await _library.ScanAsync(_folders.ToList(), progress, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            await _store.SaveMusicAsync(_library.All, cancellationToken).ConfigureAwait(false);

            return BuildSummary(processed, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Snapshot of all catalogued tracks.</summary>
    public async Task<IReadOnlyList<MusicTrack>> SnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _library.All.ToList();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Looks up one track by exact path (case-insensitive).</summary>
    public async Task<MusicTrack?> GetAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _library.TryGet(path);
        }
        finally { _gate.Release(); }
    }

    public async Task<MusicTrack?> ReanalyzeAsync(
        string path, bool force, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (_library.TryGet(path) is null)
                return null;

            if (force)
                await _library.ForceReanalyzeAsync(path, cancellationToken).ConfigureAwait(false);
            else
                await _library.ReanalyzeAsync(path, cancellationToken).ConfigureAwait(false);

            await _store.SaveMusicAsync(_library.All, cancellationToken).ConfigureAwait(false);
            return _library.TryGet(path);
        }
        finally { _gate.Release(); }
    }

    public async Task<ReanalysisSummary> ReanalyzePendingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var failures = new List<FailedTrack>();
            var service = new CatalogReanalysisService(
                _library,
                _store,
                onError: message => failures.Add(new FailedTrack(string.Empty, message)));
            ReanalysisOutcome outcome = await service.RunAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new ReanalysisSummary(
                outcome.Considered,
                outcome.Analyzed,
                _library.PathsNeedingAnalysis().Count,
                failures);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Harmonically-compatible tracks for the catalogued seed at <paramref name="path"/>.</summary>
    public async Task<(MusicTrack Seed, IReadOnlyList<MusicTrack> Matches)?> HarmonicMatchesAsync(
        string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            MusicTrack? seed = _library.TryGet(path);
            return seed is null ? null : (seed, _library.HarmonicMatches(seed));
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return;

        IReadOnlyList<MusicTrack> cached = await _store.LoadMusicAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> sampleFolders = await _store.LoadSampleFoldersAsync(cancellationToken).ConfigureAwait(false);
        _library.SetSampleFolders(sampleFolders);
        if (cached.Count > 0)
        {
            _library.Restore(cached);
            foreach (string folder in cached
                         .Select(t => Canonicalize(Path.GetDirectoryName(t.File.Path)))
                         .Where(d => d is not null)
                         .Select(d => d!)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                _folders.Add(folder);
            _logger.LogInformation("Loaded {Count} tracks from catalog cache at {Path}.", cached.Count, _store.MusicCatalogPath);
        }
        _loaded = true;
    }

    /// <summary>Normalizes a folder path to its canonical absolute form so differently-spelled
    /// paths (separators, relative segments) collapse to one identity. Returns null for blanks
    /// or paths the OS rejects.</summary>
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

    private ScanSummary BuildSummary(int processed, long elapsedMs)
    {
        IReadOnlyCollection<MusicTrack> all = _library.All;
        var failures = all
            .Where(t => t.Status == MediaAnalysisStatus.Failed)
            .Select(t => new FailedTrack(t.File.Path, t.Error ?? "unknown error"))
            .Take(50)
            .ToList();

        return new ScanSummary(
            TotalTracks: all.Count,
            Ok: all.Count(t => t.Status == MediaAnalysisStatus.Ok),
            PartiallyAnalyzed: all.Count(t => t.Status == MediaAnalysisStatus.PartiallyAnalyzed),
            Failed: all.Count(t => t.Status == MediaAnalysisStatus.Failed),
            ProcessedThisScan: processed,
            ElapsedMs: elapsedMs,
            Folders: _folders.ToList(),
            Failures: failures);
    }
}
