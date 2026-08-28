using System.Diagnostics;
using Liveolator.Core.Analysis;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Import;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
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
    private readonly IMusicCatalogStore _store;
    private readonly ILogger<LibrarySession> _logger;
    private readonly IReadOnlyList<ILibraryImporter> _importers;
    private readonly IReadOnlyList<IFolderLibraryImporter> _folderImporters;
    private readonly LibraryImportService _importService;
    private readonly ILoudnessMeter _loudnessMeter;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SortedSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public LibrarySession(
        IFileEnumerator enumerator,
        IAudioDecoder decoder,
        TrackAnalyzer analyzer,
        ITrackMetadataReader metadataReader,
        IMusicCatalogStore store,
        IEnumerable<ILibraryImporter> importers,
        IEnumerable<IFolderLibraryImporter> folderImporters,
        LibraryImportService importService,
        ILoudnessMeter loudnessMeter,
        ILogger<LibrarySession> logger)
    {
        _library = new MusicLibrary(enumerator, decoder, analyzer, metadataReader);
        _store = store;
        _loudnessMeter = loudnessMeter;
        _importers = importers.ToList();
        _folderImporters = folderImporters.ToList();
        _importService = importService;
        _logger = logger;
    }

    /// <summary>
    /// Scans (and analyzes) exactly the folders it is given, persists the catalog, and returns a summary.
    /// <para>Only the requested folders are walked. Folders scanned earlier are remembered (and reported)
    /// but not re-walked: their tracks survive because a scan no longer treats files it did not walk as
    /// deleted, so asking for one ten-file folder costs ten files of work rather than the whole catalog's
    /// (issue #3).</para>
    /// </summary>
    public async Task<ScanSummary> ScanAsync(IReadOnlyList<string> folders, bool force, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var requested = new List<string>();
            foreach (string folder in folders)
            {
                string? canonical = Canonicalize(folder);
                if (canonical is null || requested.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                    continue;
                requested.Add(canonical);
                _folders.Add(canonical);
            }

            if (requested.Count == 0)
                throw new ArgumentException("No folders to scan. Pass at least one folder path.", nameof(folders));

            // Force drops the cache only for the folders being scanned, so every file under them
            // re-analyzes while the rest of the catalog — which this scan does not speak for — stays.
            if (force)
                foreach (string path in _library.All
                             .Select(t => t.File.Path)
                             .Where(p => requested.Any(root => FolderScope.IsUnder(p, root)))
                             .ToList())
                    _library.Remove(path);

            var processed = 0;
            var progress = new Progress<ScanProgress>(p => processed = Math.Max(processed, p.Total));

            var stopwatch = Stopwatch.StartNew();
            // Persist each track as it is analyzed. Without this the only write was the whole-catalog
            // save below, so a scan cut short — a client timeout, a dropped network share — threw away
            // every track it had already analyzed. Matters most on a large SMB library, where a full
            // pass runs for hours.
            await _library.ScanAsync(
                requested, progress, cancellationToken,
                onEntryProcessed: (track, ct) => _store.SaveTrackAsync(track, ct),
                onEntryRemoved: (path, ct) => _store.DeleteTrackAsync(path, ct)).ConfigureAwait(false);
            stopwatch.Stop();

            await _store.SaveMusicAsync(_library.All, cancellationToken).ConfigureAwait(false);

            return BuildSummary(requested, processed, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Imports another DJ app's library: parses it with the named importer (a file for Rekordbox/Traktor/
    /// VirtualDJ, a folder for Serato/Mixxx), maps tracks + cues + playlists into the catalog (remapping
    /// paths against what's catalogued), merges, and persists. Returns a summary of what changed.
    /// </summary>
    public async Task<ImportSummaryDto> ImportAsync(
        string format, string path, ImportMergePolicy policy, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            LibraryImport parsed = ParseImport(format, path);
            LibraryImportResult result = await _importService
                .ImportAsync(parsed, _library.All, policy, cancellationToken).ConfigureAwait(false);

            _library.Restore(_library.All.Concat(result.TracksToUpsert).ToList());
            await _store.SaveMusicAsync(_library.All, cancellationToken).ConfigureAwait(false);
            return ImportSummaryDto.From(format, result.Summary);
        }
        finally { _gate.Release(); }
    }

    private LibraryImport ParseImport(string format, string path)
    {
        ILibraryImporter? fileImporter = _importers
            .FirstOrDefault(i => string.Equals(i.FormatName, format, StringComparison.OrdinalIgnoreCase));
        if (fileImporter is not null)
        {
            using FileStream stream = File.OpenRead(path);
            return fileImporter.Parse(stream);
        }

        IFolderLibraryImporter? folderImporter = _folderImporters
            .FirstOrDefault(i => string.Equals(i.FormatName, format, StringComparison.OrdinalIgnoreCase));
        if (folderImporter is not null)
            return folderImporter.Parse(path);

        string known = string.Join(
            ", ", _importers.Select(i => i.FormatName).Concat(_folderImporters.Select(i => i.FormatName)));
        throw new ArgumentException($"Unknown import format '{format}'. Known formats: {known}.");
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

    /// <summary>
    /// Looks up one track by exact path, falling back to a file-name match — so an agent that passes a
    /// differently-spelled path (mapped drive S:\ vs the UNC share) still finds a catalogued track, the
    /// same resilience the App's UI uses (doc 31 L5).
    /// </summary>
    public async Task<MusicTrack?> GetAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _library.TryGetByPathOrName(path);
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
            // Resolve the agent's path to the catalogued track first (path-or-name), then re-analyze by
            // its canonical path so the inner exact-path lookup hits (doc 31 L5).
            if (_library.TryGetByPathOrName(path) is not { } track)
                return null;
            string canonical = track.File.Path;

            if (force)
                await _library.ForceReanalyzeAsync(canonical, cancellationToken).ConfigureAwait(false);
            else
                await _library.ReanalyzeAsync(canonical, cancellationToken).ConfigureAwait(false);

            await _store.SaveMusicAsync(_library.All, cancellationToken).ConfigureAwait(false);
            return _library.TryGet(canonical);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Applies a hand-corrected BPM and/or key to a catalogued track, locks it against automatic
    /// re-analysis, and persists. Returns null when the path — matched by path or file name (doc 31 L5) —
    /// is not catalogued.
    /// </summary>
    public async Task<MusicTrack?> SetManualAnalysisAsync(
        string path, double? bpm, string? camelot, double? downbeatOffsetSeconds, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (_library.TryGetByPathOrName(path) is not { } track)
                return null;

            MusicTrack? updated = _library.SetManualAnalysis(track.File.Path, bpm, camelot, downbeatOffsetSeconds);
            if (updated is null)
                return null;

            await _store.SaveMusicAsync(_library.All, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Manual analysis set on {Path}: bpm {Bpm}, key {Key}, grid nudge {NudgeSeconds}s",
                track.File.Path, bpm, camelot, downbeatOffsetSeconds);
            return updated;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Measures the integrated loudness of every catalogued track that lacks it, so a set can gain each
    /// clip to one level. Independent of the analyzer version by design — see
    /// <see cref="CatalogLoudnessService"/> — so this never triggers a re-analysis.
    /// </summary>
    public async Task<LoudnessSummary> MeasureLoudnessAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var failures = new List<FailedTrack>();
            var service = new CatalogLoudnessService(
                _library,
                _loudnessMeter,
                _store,
                onError: message => failures.Add(new FailedTrack(string.Empty, message)));
            LoudnessOutcome outcome = await service.RunAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Loudness pass measured {Measured} of {Considered} tracks", outcome.Measured, outcome.Considered);
            return new LoudnessSummary(
                outcome.Considered,
                outcome.Measured,
                _library.PathsNeedingLoudness().Count,
                failures);
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
            MusicTrack? seed = _library.TryGetByPathOrName(path); // path-or-name resilience (doc 31 L5)
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
            _logger.LogInformation("Loaded {Count} tracks from the catalog cache.", cached.Count);
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

    private ScanSummary BuildSummary(IReadOnlyList<string> scanned, int processed, long elapsedMs)
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
            Folders: scanned,
            KnownFolders: _folders.ToList(),
            Failures: failures);
    }
}
