using System.IO;

namespace Liveolator.Core.Library.Music;

/// <summary>
/// Decides whether a track file can be decoded right now WITHOUT triggering a blocking fetch. A scan or
/// auto-cue pass that force-decodes an unreachable file (a disconnected share) or an un-downloaded
/// OneDrive/iCloud "online-only" placeholder will stall — touching the placeholder makes the OS download
/// the whole file on the worker thread, hanging the pass. We skip those so one bad file never freezes the
/// whole run (global standards #16/#26); the caller reports how many were skipped.
/// </summary>
public static class TrackFileReachability
{
    // Cloud-provider placeholder markers. Offline = classic offline attribute; RecallOnDataAccess
    // (0x00400000, not in the FileAttributes enum) is what OneDrive/iCloud "Files On-Demand" set on a
    // dehydrated file whose bytes aren't local yet — reading it forces a synchronous download.
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    /// <summary>
    /// True when the file exists locally and is not an un-downloaded cloud placeholder, so a decode can
    /// proceed without a blocking fetch. A path that doesn't exist, or whose attributes can't be read,
    /// returns false (skip it).
    /// </summary>
    public static bool IsLocallyDecodable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Offline | RecallOnDataAccess | RecallOnOpen)) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
