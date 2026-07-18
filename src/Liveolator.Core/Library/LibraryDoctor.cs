using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;

namespace Liveolator.Core.Library.Doctor;

public sealed class LibraryDoctor
{
    private readonly IFileExistenceProbe _files;
    private readonly IFolderExistenceProbe _folders;

    public LibraryDoctor(IFileExistenceProbe files, IFolderExistenceProbe folders)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
    }

    public LibraryDoctorReport Scan(
        IEnumerable<MusicTrack> tracks,
        IEnumerable<VisualAsset> visualAssets,
        IEnumerable<string> musicFolders,
        IEnumerable<string> visualFolders,
        IReadOnlyList<MediaIdentity>? identities = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(visualAssets);
        ArgumentNullException.ThrowIfNull(musicFolders);
        ArgumentNullException.ThrowIfNull(visualFolders);

        var trackList = tracks.ToList();
        var visualList = visualAssets.ToList();
        var issues = new List<LibraryIssue>();
        var offlineFolders = musicFolders
            .Concat(visualFolders)
            .Where(f => !string.IsNullOrWhiteSpace(f) && !_folders.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string folder in offlineFolders)
            issues.Add(new LibraryIssue(
                IdFor(LibraryIssueKind.OfflineFolder, folder),
                LibraryIssueKind.OfflineFolder,
                MediaIdentityKind.Music,
                folder,
                Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                $"Scan folder is offline or unreachable: {folder}",
                LibraryRepairConfidence.Low,
                Array.Empty<string>()));

        foreach (MusicTrack track in trackList)
        {
            LibraryIssue? issue = ClassifyTrack(track);
            if (issue is not null)
                issues.Add(issue);
        }

        foreach (VisualAsset asset in visualList)
        {
            if (!_files.Exists(asset.File.Path))
                issues.Add(IssueFor(
                    LibraryIssueKind.UnreachableVisualAsset,
                    MediaIdentityKind.Visual,
                    asset.File.Path,
                    asset.Title,
                    $"Visual asset is missing: {asset.File.Path}",
                    LibraryRepairConfidence.Medium));
        }

        var entries = trackList.Cast<IMediaEntry>().Concat(visualList).ToList();
        IReadOnlyList<DuplicateGroup<IMediaEntry>> duplicates =
            DuplicateFinder.FindWithIdentities(entries, identities ?? Array.Empty<MediaIdentity>())
                .Select(g => new DuplicateGroup<IMediaEntry>(g.Entries.Cast<IMediaEntry>().ToList(), g.Confidence))
                .ToList();

        foreach (DuplicateGroup<IMediaEntry> group in duplicates)
        {
            string firstPath = group.Entries[0].File.Path;
            issues.Add(new LibraryIssue(
                IdFor(LibraryIssueKind.DuplicateCandidate, firstPath),
                LibraryIssueKind.DuplicateCandidate,
                KindFor(group.Entries[0]),
                firstPath,
                Path.GetFileNameWithoutExtension(firstPath),
                group.Confidence == LibraryRepairConfidence.High
                    ? $"Exact duplicate group ({group.Entries.Count} files)."
                    : $"Possible duplicate group ({group.Entries.Count} files; needs review).",
                group.Confidence,
                group.Entries.Select(e => e.File.Path).ToList()));
        }

        return new LibraryDoctorReport(
            issues.OrderBy(i => i.Kind).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            duplicates,
            offlineFolders);
    }

    public static LibraryRepairPlan Preview(IReadOnlyList<LibraryRepairAction> actions, IReadOnlyList<string>? blockers = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var preview = new LibraryRepairPreview(
            actions.Count(a => a.Kind == LibraryRepairActionKind.RelocateCatalogPath),
            actions.Count(a => a.Kind == LibraryRepairActionKind.RemoveFromCatalog),
            actions.Count(a => a.Kind == LibraryRepairActionKind.MergeDuplicateCatalogEntries),
            0,
            0,
            0,
            blockers is null ? Array.Empty<string>() : blockers.ToList());
        return new LibraryRepairPlan(actions.ToList(), preview);
    }

    // Health of a single music track: a missing file, then (for present files) failed / unanalyzed /
    // low-confidence analysis — at most one issue per track. Returns null when the track is healthy.
    private LibraryIssue? ClassifyTrack(MusicTrack track)
    {
        if (!_files.Exists(track.File.Path))
            return IssueFor(
                LibraryIssueKind.MissingFile,
                MediaIdentityKind.Music,
                track.File.Path,
                track.Title,
                $"Track file is missing: {track.File.Path}",
                LibraryRepairConfidence.Medium);

        if (track.Status == MediaAnalysisStatus.Failed)
            return IssueFor(
                LibraryIssueKind.BrokenAnalysis,
                MediaIdentityKind.Music,
                track.File.Path,
                track.Title,
                track.Error is null ? "Track analysis failed." : $"Track analysis failed: {track.Error}",
                LibraryRepairConfidence.High);

        if (track.Bpm is null)
            return IssueFor(
                LibraryIssueKind.UnanalyzedTrack,
                MediaIdentityKind.Music,
                track.File.Path,
                track.Title,
                "Track has no BPM analysis.",
                LibraryRepairConfidence.High);

        if (track.Status == MediaAnalysisStatus.PartiallyAnalyzed
            || track.Bpm.Confidence < 0.35
            || track.Key?.Confidence < 0.2)
            return IssueFor(
                LibraryIssueKind.LowConfidenceAnalysis,
                MediaIdentityKind.Music,
                track.File.Path,
                track.Title,
                "Track analysis is low-confidence.",
                LibraryRepairConfidence.Medium);

        return null;
    }


    private static LibraryIssue IssueFor(
        LibraryIssueKind kind,
        MediaIdentityKind mediaKind,
        string path,
        string title,
        string message,
        LibraryRepairConfidence confidence)
        => new(IdFor(kind, path), kind, mediaKind, path, title, message, confidence, Array.Empty<string>());

    private static string IdFor(LibraryIssueKind kind, string path)
        => $"{kind}:{FolderScope.Normalize(path)}";

    private static MediaIdentityKind KindFor(IMediaEntry entry)
        => entry is VisualAsset ? MediaIdentityKind.Visual : MediaIdentityKind.Music;
}
