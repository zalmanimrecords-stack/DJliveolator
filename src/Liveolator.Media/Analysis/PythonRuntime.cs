using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Liveolator.Media.Analysis;

/// <summary>
/// Resolves the per-user Python interpreter used by the offline analysis seam (doc 32 §2.1 — download
/// on demand). The runtime is NOT bundled with the app; an in-app "Enable advanced analysis" download
/// (built later) populates the per-user dir. Until then <see cref="IsAvailable"/> is false and the
/// analyzers no-op. This type only locates the interpreter and reports presence — it does not download.
/// </summary>
public sealed class PythonRuntime
{
    private readonly string _baseDir;

    /// <param name="baseDir">Per-user Python directory. Defaults to
    /// <c>%APPDATA%\Liveolator\python</c> (Windows) or <c>~/.local/share/Liveolator/python</c> (Mac/Linux).</param>
    public PythonRuntime(string? baseDir = null)
        => _baseDir = string.IsNullOrWhiteSpace(baseDir) ? DefaultBaseDir() : baseDir;

    /// <summary>The per-user directory the runtime lives in.</summary>
    public string BaseDir => _baseDir;

    /// <summary>Full path to the Python interpreter inside the per-user dir (may not exist yet).</summary>
    public string InterpreterPath => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(_baseDir, "python.exe")
        : Path.Combine(_baseDir, "bin", "python3");

    /// <summary>True when the interpreter is present on disk (the download has run).</summary>
    public bool IsAvailable => File.Exists(InterpreterPath);

    private static string DefaultBaseDir()
    {
        // APPDATA on Windows; the XDG data dir / its fallback elsewhere.
        string root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liveolator");

        // On non-Windows, LocalApplicationData already maps under ~/.local/share or ~/.config; on Windows
        // APPDATA is the roaming root and we add the app folder. Normalize so the path ends in .../Liveolator/python.
        string appDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(root, "Liveolator")
            : root;
        return Path.Combine(appDir, "python");
    }
}
