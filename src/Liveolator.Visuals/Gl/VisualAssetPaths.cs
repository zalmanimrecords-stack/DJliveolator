namespace Liveolator.Visuals.Gl;

/// <summary>
/// The single on-disk location for built-in generator shader caches and baked images.
///
/// Consolidated under the per-user Roaming root (<c>%APPDATA%\Liveolator\assets</c>) so ALL visual
/// content lives under one <c>%APPDATA%\Liveolator</c> parent alongside <c>frktl-presets</c>,
/// <c>extensions</c>, and <c>control-skins</c> - instead of being split across two roots. Earlier builds
/// wrote these caches under <c>%LocalAppData%\Liveolator\assets</c>; that folder is migrated once on
/// first use so nothing is left behind in the old location.
/// </summary>
public static class VisualAssetPaths
{
    private const string FolderName = "Liveolator";
    private const string AssetsName = "assets";

    private static int _migrated;

    /// <summary>
    /// The consolidated assets directory under Roaming <c>%APPDATA%</c>. The legacy Local cache is moved
    /// here once per process on first call. Callers create the directory on demand (as they already did).
    /// </summary>
    public static string Default()
    {
        string newRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName, AssetsName);
        string oldRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName, AssetsName);

        // Migrate at most once per process; the regenerable caches don't need repeated sweeps.
        if (System.Threading.Interlocked.Exchange(ref _migrated, 1) == 0)
            TryMigrate(oldRoot, newRoot);

        return newRoot;
    }

    /// <summary>
    /// Best-effort one-time move of the legacy Local assets cache into <paramref name="newRoot"/>, then
    /// removal of the now-redundant old folder so the content lives in exactly one place. Tolerant by
    /// design: these files are regenerable (each add-on rewrites its shader/image on startup), so any IO
    /// failure is harmless - the missing file is simply recreated at <paramref name="newRoot"/>. Exposed
    /// for tests.
    /// </summary>
    public static void TryMigrate(string oldRoot, string newRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRoot);
        try
        {
            if (!Directory.Exists(oldRoot)
                || string.Equals(Path.GetFullPath(oldRoot), Path.GetFullPath(newRoot), StringComparison.OrdinalIgnoreCase))
                return;

            Directory.CreateDirectory(newRoot);
            foreach (string source in Directory.EnumerateFiles(oldRoot, "*", SearchOption.TopDirectoryOnly))
            {
                string destination = Path.Combine(newRoot, Path.GetFileName(source));
                if (!File.Exists(destination))
                    File.Copy(source, destination);
            }

            try { Directory.Delete(oldRoot, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* leave the orphan */ }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The regenerable caches will simply be recreated at newRoot; never block startup (global #16/#26).
        }
    }
}
