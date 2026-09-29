using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Liveolator.Media;

/// <summary>
/// Detects Windows package (MSIX) file-system virtualization of a data folder. A process launched from a
/// packaged host — Claude desktop, for one — sees a merged AppData and has its writes redirected into the
/// host's private <c>Packages\...\LocalCache</c>. Such a process silently works on a fork of the
/// catalog, and once the fork's own WAL is gone it can pair with the real App's WAL and corrupt it.
/// </summary>
/// <remarks>
/// A package-identity check does not work: those processes report no package. The only reliable signal
/// is where a newly created file really lands, so the probe creates one (deleted on close) and asks the
/// OS for its final path. It never opens catalog.db, because opening that for write is what forks it.
/// </remarks>
public static class AppDataRedirection
{
    /// <summary>
    /// The folder writes to <paramref name="directory"/> really land in when it is redirected, or null
    /// when it is not, the OS is not Windows, or the probe could not run (reported via
    /// <paramref name="onWarning"/>).
    /// </summary>
    public static string? DetectRedirect(string directory, Action<string>? onWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, $".redirect-probe-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            string? finalPath = FinalPath(stream.SafeFileHandle);
            return finalPath is not null && IsPackageRedirect(directory, finalPath)
                ? Path.GetDirectoryName(finalPath)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            onWarning?.Invoke($"Could not check '{directory}' for package virtualization ({ex.Message}).");
            return null;
        }
    }

    /// <summary>
    /// True when a file created under <paramref name="requestedDirectory"/> landed in a package's private
    /// LocalCache. Compared by shape, not by string equality, because the requested path may use 8.3
    /// short names (<c>SIMONR~1</c>) that the final path never does.
    /// </summary>
    public static bool IsPackageRedirect(string requestedDirectory, string finalPath)
    {
        static bool InPackageCache(string path)
            => path.Contains(@"\AppData\Local\Packages\", StringComparison.OrdinalIgnoreCase)
               && path.Contains(@"\LocalCache\", StringComparison.OrdinalIgnoreCase);

        return InPackageCache(finalPath) && !InPackageCache(requestedDirectory);
    }

    private static string? FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(1024);
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity)
            return null;
        string path = buffer.ToString();
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);
}
