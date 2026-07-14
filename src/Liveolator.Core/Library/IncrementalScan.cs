namespace Liveolator.Core.Library;

/// <summary>How a scanned file changed relative to the known catalog.</summary>
public enum ScanChange
{
    Added,
    Modified,
    Unchanged,
    Removed
}

/// <summary>A single change between the current filesystem state and the known catalog.</summary>
public readonly record struct ScanDelta(ScanChange Change, ScannedFile File);

/// <summary>
/// Pure incremental-scan diff shared by every media library: compares the current file set
/// against known fingerprints and classifies each as Added / Modified / Unchanged / Removed.
/// </summary>
public static class IncrementalScan
{
    public static IReadOnlyList<ScanDelta> Diff(
        IEnumerable<ScannedFile> current,
        IReadOnlyDictionary<string, FileFingerprint> known)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(known);

        var deltas = new List<ScanDelta>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ScannedFile file in current)
        {
            seen.Add(file.Path);
            if (!known.TryGetValue(file.Path, out FileFingerprint fp))
                deltas.Add(new ScanDelta(ScanChange.Added, file));
            else if (fp != FileFingerprint.Of(file))
                deltas.Add(new ScanDelta(ScanChange.Modified, file));
            else
                deltas.Add(new ScanDelta(ScanChange.Unchanged, file));
        }

        foreach (KeyValuePair<string, FileFingerprint> entry in known)
        {
            if (!seen.Contains(entry.Key))
            {
                var removed = new ScannedFile(entry.Key, entry.Value.SizeBytes, entry.Value.LastModifiedUtc);
                deltas.Add(new ScanDelta(ScanChange.Removed, removed));
            }
        }

        return deltas;
    }
}
