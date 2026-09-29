namespace Liveolator.Core.Persistence;

/// <summary>
/// The persisted catalog exists but is damaged. Unlike a missing or outdated catalog this is never
/// answered with an empty result: an empty library invites a re-scan that writes into the damaged file,
/// so the store refuses every read and write until the file is replaced.
/// </summary>
public sealed class CatalogCorruptException : IOException
{
    public CatalogCorruptException(string path, string detail, Exception? inner = null)
        : base($"The catalog '{path}' is damaged ({detail}). Nothing will be read from or written to it; "
               + "restore it from a backup or move it aside to start a new one.", inner)
    {
        CatalogPath = path;
    }

    public string CatalogPath { get; }
}
