using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis.Cues;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using PlaylistRecord = Liveolator.Core.Playlist.Playlist;

namespace Liveolator.Core.Library.Import;

/// <summary>
/// Maps a parsed <see cref="LibraryImport"/> into Liveolator: remaps each source path to a local file,
/// builds/merges catalog tracks (per <see cref="ImportMergePolicy"/>), and persists hot cues + playlists
/// through the existing stores — no new on-disk format, no schema change. The catalog write itself stays
/// with the caller (it owns the in-memory <c>MusicLibrary</c>), which merges <see cref="LibraryImportResult.TracksToUpsert"/>
/// and persists exactly as a scan does. Pure orchestration over seams, so it unit-tests with fakes.
/// </summary>
public sealed class LibraryImportService
{
    private readonly IHotCueStore _hotCueStore;
    private readonly IPlaylistStore _playlistStore;
    private readonly Func<string, ScannedFile?> _stat;

    /// <param name="hotCueStore">Where imported hot cues are persisted (per track path).</param>
    /// <param name="playlistStore">Where imported playlists are persisted.</param>
    /// <param name="stat">Probes a path: returns its <see cref="ScannedFile"/> if it exists, else null.</param>
    public LibraryImportService(
        IHotCueStore hotCueStore, IPlaylistStore playlistStore, Func<string, ScannedFile?> stat)
    {
        _hotCueStore = hotCueStore ?? throw new ArgumentNullException(nameof(hotCueStore));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
        _stat = stat ?? throw new ArgumentNullException(nameof(stat));
    }

    public async Task<LibraryImportResult> ImportAsync(
        LibraryImport import,
        IReadOnlyCollection<MusicTrack> catalog,
        ImportMergePolicy policy = ImportMergePolicy.FillGaps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(catalog);

        var resolver = new ImportPathResolver(catalog, _stat);
        var catalogByPath = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);
        foreach (MusicTrack t in catalog)
            catalogByPath[t.File.Path] = t;

        var tracksToUpsert = new List<MusicTrack>();
        var sourceToLocal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0, unresolved = 0, cuesImported = 0, cuesSkipped = 0;

        foreach (ImportedTrack src in import.Tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScannedFile? resolved = resolver.Resolve(src.SourcePath, src.DurationSeconds);
            if (resolved is not { } file)
            {
                unresolved++;
                continue;
            }

            string local = file.Path;
            sourceToLocal[src.SourcePath] = local;

            catalogByPath.TryGetValue(local, out MusicTrack? existing);
            MusicTrack merged = ImportTrackMapper.Map(src, file, existing, policy);
            if (existing is null)
            {
                tracksToUpsert.Add(merged);
                added++;
            }
            else if (merged != existing)
            {
                tracksToUpsert.Add(merged);
                updated++;
            }

            await ImportCuesAsync(src, local, policy, cancellationToken, () => cuesImported++, () => cuesSkipped++)
                .ConfigureAwait(false);
        }

        (int playlistsImported, int refsDropped) =
            await ImportPlaylistsAsync(import, sourceToLocal, resolver, cancellationToken).ConfigureAwait(false);

        var summary = new LibraryImportSummary(
            added, updated, unresolved, cuesImported, cuesSkipped, playlistsImported, refsDropped);
        return new LibraryImportResult(tracksToUpsert, summary);
    }

    private async Task ImportCuesAsync(
        ImportedTrack src, string localPath, ImportMergePolicy policy, CancellationToken cancellationToken,
        Action onImported, Action onSkipped)
    {
        TrackCueSet cueSet = ImportCueMapper.Map(src, out _);
        bool hasCues = cueSet.HotCues.Count > 0 || cueSet.PrimaryCueSamples is not null;
        if (!hasCues)
            return;

        if (policy == ImportMergePolicy.FillGaps)
        {
            TrackCueRecord? existing = await _hotCueStore.LoadAsync(localPath, cancellationToken).ConfigureAwait(false);
            if (existing is { HotCues.Count: > 0 })
            {
                onSkipped();
                return;
            }
        }

        await _hotCueStore.SaveAsync(TrackCueRecord.FromCueSet(localPath, cueSet), cancellationToken).ConfigureAwait(false);
        onImported();
    }

    private async Task<(int Imported, int RefsDropped)> ImportPlaylistsAsync(
        LibraryImport import, IReadOnlyDictionary<string, string> sourceToLocal, ImportPathResolver resolver,
        CancellationToken cancellationToken)
    {
        int imported = 0, refsDropped = 0;
        foreach (ImportedPlaylist playlist in import.Playlists)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var paths = new List<string>();
            foreach (string sourcePath in playlist.SourceTrackPaths)
            {
                string? local = sourceToLocal.TryGetValue(sourcePath, out string? known)
                    ? known
                    : resolver.Resolve(sourcePath, null)?.Path;
                if (local is not null)
                    paths.Add(local);
                else
                    refsDropped++;
            }

            if (paths.Count == 0)
                continue;

            await _playlistStore.SaveAsync(new PlaylistRecord(playlist.Name, paths), cancellationToken)
                .ConfigureAwait(false);
            imported++;
        }

        return (imported, refsDropped);
    }
}
