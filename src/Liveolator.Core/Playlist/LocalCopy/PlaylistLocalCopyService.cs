using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Liveolator.Core.Playlist.LocalCopy;

/// <summary>
/// Takes a saved playlist offline for a gig away from the NAS: copies its files to a local folder, gives
/// each copy a catalog row carrying the source's analysis and a copy of its hot cues (everything is keyed
/// by path, so without them a copy is an unanalysed stranger), and saves a "(local)" playlist pointing at
/// the copies. The original playlist and the NAS rows are left alone.
/// </summary>
/// <remarks>
/// Re-running is safe and resumes: files already there are reused, and a local row or cue set that already
/// exists wins over the NAS one, so edits made at a gig survive a later refresh. The caller owns the
/// in-memory library and the scan-folder list and must register the destination as a library folder, or
/// removing another folder later prunes the local rows.
/// </remarks>
public sealed class PlaylistLocalCopyService
{
    public const string LocalSuffix = " (local)";

    private readonly IFileCopier _copier;
    private readonly IMusicCatalogStore _catalogStore;
    private readonly IHotCueStore _hotCueStore;
    private readonly IPlaylistStore _playlistStore;
    private readonly ILogger _logger;

    public PlaylistLocalCopyService(
        IFileCopier copier,
        IMusicCatalogStore catalogStore,
        IHotCueStore hotCueStore,
        IPlaylistStore playlistStore,
        ILogger<PlaylistLocalCopyService>? logger = null)
    {
        _copier = copier ?? throw new ArgumentNullException(nameof(copier));
        _catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
        _hotCueStore = hotCueStore ?? throw new ArgumentNullException(nameof(hotCueStore));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<LocalCopyResult> CopyAsync(
        Playlist playlist,
        IReadOnlyCollection<MusicTrack> catalog,
        IReadOnlyList<string> scanFolders,
        string destinationRoot,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var catalogByPath = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);
        foreach (MusicTrack track in catalog)
            catalogByPath[track.File.Path] = track;

        IReadOnlyList<LocalCopyItem> plan =
            LocalCopyPlanner.Plan(playlist, catalogByPath, scanFolders, destinationRoot, _copier.TryStat);
        long bytesToCopy = LocalCopyPlanner.BytesToCopy(plan);
        long bytesFree = _copier.AvailableFreeBytes(destinationRoot);
        var problems = plan
            .Where(i => i.DestinationPath is null)
            .Select(i => new LocalCopyProblem(i.SourcePath, Describe(i.Step)))
            .ToList();

        if (bytesToCopy > bytesFree)
        {
            _logger.LogWarning(
                "Local copy of {Playlist} needs {Needed} bytes but {Destination} has {Free}; nothing copied",
                playlist.Name, bytesToCopy, destinationRoot, bytesFree);
            return new LocalCopyResult(null, Array.Empty<MusicTrack>(), 0, 0, problems, bytesToCopy, bytesFree);
        }

        List<LocalCopyItem> work = plan.Where(i => i.DestinationPath is not null).ToList();
        var localPaths = new List<string>(work.Count);
        var localTracks = new List<MusicTrack>();
        int copied = 0, alreadyPresent = 0;

        for (int done = 0; done < work.Count; done++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LocalCopyItem item = work[done];
            progress?.Report(new ScanProgress(done, work.Count, item.SourcePath));
            try
            {
                if (await TakeOfflineAsync(item, catalogByPath, cancellationToken).ConfigureAwait(false) is { } row)
                    localTracks.Add(row);
                localPaths.Add(item.DestinationPath!);
                if (item.Step == LocalCopyStep.Copy)
                    copied++;
                else
                    alreadyPresent++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Local copy of {Source} to {Destination} failed", item.SourcePath, item.DestinationPath);
                problems.Add(new LocalCopyProblem(item.SourcePath, ex.Message));
            }
        }
        progress?.Report(new ScanProgress(work.Count, work.Count, string.Empty));

        Playlist? localPlaylist = null;
        if (localPaths.Count > 0)
        {
            string name = playlist.Name.EndsWith(LocalSuffix, StringComparison.OrdinalIgnoreCase)
                ? playlist.Name
                : playlist.Name + LocalSuffix;
            localPlaylist = new Playlist(name, localPaths);
            await _playlistStore.SaveAsync(localPlaylist, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Local copy of {Playlist}: {Copied} copied, {Present} already present, {Problems} left out",
            playlist.Name, copied, alreadyPresent, problems.Count);
        return new LocalCopyResult(localPlaylist, localTracks, copied, alreadyPresent, problems, bytesToCopy, bytesFree);
    }

    private async Task<MusicTrack?> TakeOfflineAsync(
        LocalCopyItem item, IReadOnlyDictionary<string, MusicTrack> catalogByPath, CancellationToken cancellationToken)
    {
        string destination = item.DestinationPath!;
        ScannedFile file = item.Step == LocalCopyStep.Copy
            ? await _copier.CopyAsync(item.SourcePath, destination, cancellationToken).ConfigureAwait(false)
            : _copier.TryStat(destination) ?? throw new IOException($"The local file {destination} is gone.");

        // From here the file is on disk; its row and cues are written even if a cancel arrives, so a
        // copied file is never left without its analysis.
        MusicTrack? row = null;
        if (!catalogByPath.ContainsKey(destination))
        {
            row = item.Track! with { File = file };
            await _catalogStore.SaveTrackAsync(row, CancellationToken.None).ConfigureAwait(false);
        }

        if (await _hotCueStore.LoadAsync(destination, CancellationToken.None).ConfigureAwait(false) is null
            && await _hotCueStore.LoadAsync(item.SourcePath, CancellationToken.None).ConfigureAwait(false) is { } cues)
            await _hotCueStore.SaveAsync(cues with { TrackPath = destination }, CancellationToken.None).ConfigureAwait(false);

        return row;
    }

    private static string Describe(LocalCopyStep step) => step switch
    {
        LocalCopyStep.NotCatalogued => "Not in the library, so there is no analysis to copy. Scan its folder first.",
        LocalCopyStep.NameClash => "Another track in this playlist, from a different folder, has the same local path.",
        LocalCopyStep.UnsafePath => "The path contains '.' or '..', so it was not copied.",
        _ => step.ToString(),
    };
}
