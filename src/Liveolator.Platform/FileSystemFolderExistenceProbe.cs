using Liveolator.Core.Library;

namespace Liveolator.Platform;

public sealed class FileSystemFolderExistenceProbe : IFolderExistenceProbe
{
    public bool Exists(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return false;

        try
        {
            return Directory.Exists(folder);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

