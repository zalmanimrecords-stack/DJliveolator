using System.Security.Cryptography;
using System.Text;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;

namespace Liveolator.Core.Library.Doctor;

public static class MediaIdentityBuilder
{
    public static IReadOnlyList<MediaIdentity> FromCatalog(
        IEnumerable<MusicTrack> tracks,
        IEnumerable<VisualAsset> visualAssets,
        DateTime seenUtc,
        IReadOnlyDictionary<string, string?>? shaByPath = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(visualAssets);

        var identities = new List<MediaIdentity>();
        foreach (MusicTrack track in tracks)
            identities.Add(FromEntry(track, MediaIdentityKind.Music, seenUtc, shaByPath));
        foreach (VisualAsset asset in visualAssets)
            identities.Add(FromEntry(asset, MediaIdentityKind.Visual, seenUtc, shaByPath));
        return identities;
    }

    public static MediaIdentity FromEntry(
        IMediaEntry entry,
        MediaIdentityKind kind,
        DateTime seenUtc,
        IReadOnlyDictionary<string, string?>? shaByPath = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string path = entry.File.Path;
        string? sha = null;
        shaByPath?.TryGetValue(path, out sha);

        return new MediaIdentity(
            StableIdFor(kind, path),
            kind,
            new[] { path },
            Path.GetFileName(path),
            entry.File.SizeBytes,
            entry.File.LastModifiedUtc,
            NormalizeSha(sha),
            entry.Status,
            seenUtc);
    }

    public static string StableIdFor(MediaIdentityKind kind, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string input = $"{kind}:{FolderScope.Normalize(path)}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeSha(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

