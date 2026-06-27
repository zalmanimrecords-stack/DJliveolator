using System.Security.Cryptography;
using Liveolator.Core.Library;

namespace Liveolator.Media;

public sealed class Sha256FileContentHasher : IFileContentHasher
{
    public async Task<string?> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
            using var sha = SHA256.Create();
            byte[] hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

