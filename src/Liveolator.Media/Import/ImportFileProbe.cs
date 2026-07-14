using System;
using System.IO;
using System.Security;
using Liveolator.Core.Library;

namespace Liveolator.Media.Import;

/// <summary>
/// The single file-reachability probe shared by every composition root that wires the library importer
/// (the App shell and the MCP server). Returns the file's <see cref="ScannedFile"/> fingerprint
/// (size + mtime) when it exists, else null — a source path that doesn't resolve here is then remapped by
/// filename against the catalog. This is the lone OS touch in the import path, kept in one place so the two
/// roots can't drift, and so a real I/O error (permission, offline share, bad path) is surfaced via
/// <paramref name="onWarning"/> rather than swallowed silently (global standards #16/#26).
/// </summary>
public static class ImportFileProbe
{
    public static ScannedFile? Stat(string path, Action<string>? onWarning = null)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new ScannedFile(info.FullName, info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException
               or ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path we cannot stat (permission denied, offline drive, malformed path) is treated as
            // "not present" so the importer falls back to filename remap — but we report it, never drop it.
            onWarning?.Invoke($"Could not read import file '{path}': {ex.Message}");
            return null;
        }
    }
}
