namespace Liveolator.Media;

/// <summary>
/// Keeps the newest few verified copies of the catalog under <c>&lt;root&gt;/backups</c>. A copy exists only
/// once it has passed an integrity check, so every file here is safe to restore from.
/// </summary>
public sealed class CatalogBackup
{
    private readonly string _root;
    private readonly int _keep;

    public CatalogBackup(string rootDirectory, int keep = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        _root = rootDirectory;
        _keep = keep;
    }

    public string BackupDirectory => Path.Combine(_root, "backups");

    /// <summary>
    /// Writes a verified copy stamped with <paramref name="nowUtc"/> and prunes older copies beyond the
    /// kept count. Returns the new copy, or null when there is no catalog yet. Throws
    /// <see cref="Core.Persistence.CatalogCorruptException"/> when the catalog is damaged, in which case
    /// the existing backups are left untouched.
    /// </summary>
    public string? CreateVerified(DateTime nowUtc)
    {
        string source = Path.Combine(_root, "catalog.db");
        if (!File.Exists(source))
            return null;

        string target = Path.Combine(BackupDirectory, $"catalog-{nowUtc:yyyyMMdd-HHmmss}.db");
        string pending = target + ".tmp";
        SqliteCatalogSnapshot.Copy(source, pending);
        File.Move(pending, target, overwrite: true);

        foreach (string stale in Directory.GetFiles(BackupDirectory, "catalog-*.db")
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Skip(_keep))
            File.Delete(stale);

        return target;
    }
}
