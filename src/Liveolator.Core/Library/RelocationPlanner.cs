using System.IO;
using Liveolator.Core.Library.Doctor;

namespace Liveolator.Core.Library;

/// <summary>
/// Pure (no IO) matcher that proposes how to relocate missing catalogued files to copies found
/// under a new root folder. A missing file is matched to a candidate by the same identity key used
/// to detect duplicates — file name (case-insensitive) + byte size — so analysis is preserved even
/// though the folder (and modification time) changed, e.g. when an offline network drive is replaced
/// by a local copy. Folder enumeration and existence checks happen at the edges (platform seams);
/// this class only decides the mapping.
/// </summary>
public static class RelocationPlanner
{
    /// <summary>
    /// Matches each <paramref name="missing"/> entry to a <paramref name="candidate"/> with the same
    /// identity key. Each candidate is consumed by at most one missing entry (a candidate matched to
    /// one missing file is not reused for another), so the plan never proposes two entries onto the
    /// same new file. Entries with no available candidate are returned in <see cref="RelocationPlan.Unmatched"/>.
    /// </summary>
    public static RelocationPlan Plan(
        IEnumerable<ScannedFile> missing,
        IEnumerable<ScannedFile> candidates)
    {
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(candidates);

        // Group candidates by identity so each missing entry is an O(1) lookup, and a queue per key
        // lets us hand out each physical candidate to only one missing entry.
        var byIdentity = new Dictionary<FileIdentity, Queue<ScannedFile>>();
        foreach (ScannedFile candidate in candidates)
        {
            FileIdentity identity = FileIdentity.Of(candidate);
            if (!byIdentity.TryGetValue(identity, out Queue<ScannedFile>? queue))
                byIdentity[identity] = queue = new Queue<ScannedFile>();
            queue.Enqueue(candidate);
        }

        var matches = new List<RelocationMatch>();
        var unmatched = new List<ScannedFile>();

        foreach (ScannedFile entry in missing)
        {
            FileIdentity identity = FileIdentity.Of(entry);
            if (byIdentity.TryGetValue(identity, out Queue<ScannedFile>? queue) && queue.Count > 0)
                matches.Add(new RelocationMatch(entry.Path, queue.Dequeue()));
            else
                unmatched.Add(entry);
        }

        return new RelocationPlan(matches, unmatched);
    }

    /// <summary>Plans relocation by exact SHA-256 first, then by the legacy file-name+size heuristic.</summary>
    public static RelocationPlan PlanSmart(
        IEnumerable<MissingMediaFile> missing,
        IEnumerable<RelocationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(candidates);

        var remaining = new List<MissingMediaFile>(missing);
        var candidateList = new List<RelocationCandidate>(candidates);
        var matches = new List<RelocationMatch>();
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, Queue<RelocationCandidate>> bySha = candidateList
            .Where(c => !string.IsNullOrWhiteSpace(c.Sha256))
            .GroupBy(c => NormalizeSha(c.Sha256!)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new Queue<RelocationCandidate>(g), StringComparer.OrdinalIgnoreCase);

        for (int i = remaining.Count - 1; i >= 0; i--)
        {
            MissingMediaFile item = remaining[i];
            string? sha = NormalizeSha(item.Sha256);
            if (sha is null || !bySha.TryGetValue(sha, out Queue<RelocationCandidate>? queue))
                continue;

            RelocationCandidate? candidate = DequeueAvailable(queue, consumed);
            if (candidate is null)
                continue;

            consumed.Add(candidate.File.Path);
            matches.Add(new RelocationMatch(item.File.Path, candidate.File, LibraryRepairConfidence.High));
            remaining.RemoveAt(i);
        }

        RelocationPlan fallback = Plan(
            remaining.Select(m => m.File),
            candidateList.Where(c => !consumed.Contains(c.File.Path)).Select(c => c.File));
        matches.AddRange(fallback.Matches.Select(m => m with { Confidence = LibraryRepairConfidence.Low }));

        return new RelocationPlan(
            matches.OrderByDescending(m => m.Confidence).ThenBy(m => m.OldPath, StringComparer.OrdinalIgnoreCase).ToList(),
            fallback.Unmatched);
    }

    /// <summary>Plans sibling-folder relocation from one known old-path/new-file pair.</summary>
    public static RelocationPlan PlanSiblingFolder(
        IEnumerable<ScannedFile> missing,
        string oldKnownPath,
        ScannedFile newKnownFile,
        IEnumerable<ScannedFile> candidates)
    {
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldKnownPath);
        ArgumentNullException.ThrowIfNull(candidates);

        string? oldRoot = DirectoryPart(FolderScope.Normalize(oldKnownPath));
        string? newRoot = DirectoryPart(FolderScope.Normalize(newKnownFile.Path));
        if (string.IsNullOrWhiteSpace(oldRoot) || string.IsNullOrWhiteSpace(newRoot))
            return new RelocationPlan(Array.Empty<RelocationMatch>(), missing.ToList());

        Dictionary<string, ScannedFile> byPath = candidates
            .GroupBy(c => FolderScope.Normalize(c.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var matches = new List<RelocationMatch>();
        var unmatched = new List<ScannedFile>();
        foreach (ScannedFile item in missing)
        {
            string normalized = FolderScope.Normalize(item.Path);
            if (!FolderScope.IsUnderNormalized(normalized, oldRoot))
            {
                unmatched.Add(item);
                continue;
            }

            string relative = normalized.Length == oldRoot.Length
                ? Path.GetFileName(normalized)
                : normalized[(oldRoot.Length + 1)..];
            string proposed = FolderScope.Normalize(Path.Combine(newRoot, relative));
            if (byPath.TryGetValue(proposed, out ScannedFile candidate)
                && candidate.SizeBytes == item.SizeBytes
                && string.Equals(Path.GetFileName(candidate.Path), Path.GetFileName(item.Path), StringComparison.OrdinalIgnoreCase))
                matches.Add(new RelocationMatch(item.Path, candidate, LibraryRepairConfidence.Medium));
            else
                unmatched.Add(item);
        }

        return new RelocationPlan(matches, unmatched);
    }

    /// <summary>Identity key for matching files across folders: file name (case-insensitive) + byte size.</summary>
    private readonly record struct FileIdentity(string FileName, long SizeBytes)
    {
        public static FileIdentity Of(ScannedFile file)
            => new(Path.GetFileName(file.Path).ToLowerInvariant(), file.SizeBytes);
    }

    private static string? NormalizeSha(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static RelocationCandidate? DequeueAvailable(
        Queue<RelocationCandidate> queue,
        HashSet<string> consumed)
    {
        while (queue.Count > 0)
        {
            RelocationCandidate candidate = queue.Dequeue();
            if (!consumed.Contains(candidate.File.Path))
                return candidate;
        }

        return null;
    }

    private static string? DirectoryPart(string normalizedPath)
    {
        int slash = normalizedPath.LastIndexOf('/');
        if (slash <= 0)
            return null;
        return normalizedPath[..slash];
    }
}
