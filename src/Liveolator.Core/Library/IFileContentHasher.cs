namespace Liveolator.Core.Library;

public interface IFileContentHasher
{
    Task<string?> ComputeSha256Async(string path, CancellationToken cancellationToken = default);
}

