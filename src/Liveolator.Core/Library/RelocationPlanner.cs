using System.IO;

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

    /// <summary>Identity key for matching files across folders: file name (case-insensitive) + byte size.</summary>
    private readonly record struct FileIdentity(string FileName, long SizeBytes)
    {
        public static FileIdentity Of(ScannedFile file)
            => new(Path.GetFileName(file.Path).ToLowerInvariant(), file.SizeBytes);
    }
}
