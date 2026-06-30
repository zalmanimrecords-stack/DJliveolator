using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Liveolator.Core.Analysis.Stems;

namespace Liveolator.Media.Analysis;

/// <summary>
/// Owns the MANDATORY local stem cache (doc 32 §2.3): one folder per track, keyed by a stable hash of
/// the source path, holding the four FLAC stems plus a small <c>manifest.json</c>. Stems are never
/// written to / decoded from a network path — this cache dir is always local. The store resolves a
/// cache hit (skip re-separation), exposes the per-track output dir a separator writes into, and
/// persists/loads the manifest. No Python, no subprocess — pure filesystem.
/// </summary>
public sealed class StemStore
{
    private const string ManifestName = "manifest.json";
    private readonly string _cacheRoot;

    /// <param name="cacheRoot">Local cache root. Defaults to <c>%LOCALAPPDATA%\Liveolator\stems</c>
    /// (Windows) or the platform local-data equivalent. MUST be local, never a network drive.</param>
    public StemStore(string? cacheRoot = null)
        => _cacheRoot = string.IsNullOrWhiteSpace(cacheRoot) ? DefaultCacheRoot() : cacheRoot;

    /// <summary>The per-track folder for <paramref name="sourcePath"/> (created on demand by the separator).</summary>
    public string FolderFor(string sourcePath)
        => Path.Combine(_cacheRoot, HashKey(sourcePath));

    /// <summary>Path the manifest is written to / read from for a track.</summary>
    public string ManifestPathFor(string sourcePath)
        => Path.Combine(FolderFor(sourcePath), ManifestName);

    /// <summary>
    /// Returns the cached, complete <see cref="StemSet"/> for <paramref name="sourcePath"/>, or <c>null</c>
    /// on a miss (no manifest, unreadable, incomplete, or any stem file missing on disk). A cache hit lets
    /// the caller skip re-separation.
    /// </summary>
    public StemSet? TryLoad(string sourcePath)
    {
        string manifestPath = ManifestPathFor(sourcePath);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(manifestPath))
            return null;

        try
        {
            StemSet? set = StemManifestParser.Parse(File.ReadAllText(manifestPath), sourcePath);
            if (set is null)
                return null;

            // Every stem file the manifest references must still exist locally, or it is a miss.
            foreach (string path in set.StemPaths.Values)
                if (!File.Exists(path))
                    return null;

            return set;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes the manifest for <paramref name="set"/> into its per-track folder.</summary>
    public void Save(StemSet set)
    {
        string folder = FolderFor(set.SourcePath);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ManifestName), StemManifestParser.Serialize(set));
    }

    /// <summary>Stable, filesystem-safe key for a source path (SHA-256, hex). Path-only — content not read.</summary>
    private static string HashKey(string sourcePath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath));
        return Convert.ToHexString(hash);
    }

    private static string DefaultCacheRoot()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Liveolator", "stems");
}
