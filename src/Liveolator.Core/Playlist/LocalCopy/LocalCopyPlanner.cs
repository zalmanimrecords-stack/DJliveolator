using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;

namespace Liveolator.Core.Playlist.LocalCopy;

/// <summary>
/// Decides, without touching the disk, where each playlist entry's local copy goes and whether it still
/// needs copying. A track keeps its path below the library folder it was scanned from, re-rooted under
/// <c>destination/&lt;folder name&gt;</c>, so an album folder stays an album folder on the laptop.
/// </summary>
public static class LocalCopyPlanner
{
    public static IReadOnlyList<LocalCopyItem> Plan(
        Playlist playlist,
        IReadOnlyDictionary<string, MusicTrack> catalogByPath,
        IReadOnlyList<string> scanFolders,
        string destinationRoot,
        Func<string, ScannedFile?> statDestination)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(catalogByPath);
        ArgumentNullException.ThrowIfNull(scanFolders);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(statDestination);

        var items = new List<LocalCopyItem>();
        var sourceByDestination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string source in playlist.TrackPaths)
        {
            // Rebase and IsUnder compare prefixes only, so "<share>\a\..\..\x" would pass both and the
            // copy would be written wherever the dots resolve. Catalog paths can come from a snapshot on
            // a share other machines write to, so they are not trusted here.
            if (HasDotSegment(source))
            {
                items.Add(new LocalCopyItem(source, null, LocalCopyStep.UnsafePath, null));
                continue;
            }

            if (!catalogByPath.TryGetValue(source, out MusicTrack? track))
            {
                items.Add(new LocalCopyItem(source, null, LocalCopyStep.NotCatalogued, null));
                continue;
            }

            string destination = FolderScope.IsUnder(source, destinationRoot)
                ? source
                : DestinationFor(source, scanFolders, destinationRoot);

            if (sourceByDestination.TryGetValue(destination, out string? claimedBy))
            {
                if (!string.Equals(claimedBy, source, StringComparison.OrdinalIgnoreCase))
                    items.Add(new LocalCopyItem(source, null, LocalCopyStep.NameClash, track));
                continue; // a repeated entry is planned once
            }
            sourceByDestination[destination] = source;

            bool present = destination == source
                || statDestination(destination) is { } existing && existing.SizeBytes == track.File.SizeBytes;
            items.Add(new LocalCopyItem(
                source, destination, present ? LocalCopyStep.AlreadyPresent : LocalCopyStep.Copy, track));
        }

        return items;
    }

    public static long BytesToCopy(IEnumerable<LocalCopyItem> items)
        => items.Where(i => i.Step == LocalCopyStep.Copy).Sum(i => i.Track!.File.SizeBytes);

    private static bool HasDotSegment(string path)
        => path.Split('/', '\\').Any(segment => segment is "." or "..");

    private static string DestinationFor(string source, IReadOnlyList<string> scanFolders, string destinationRoot)
    {
        // Same rule PortablePath.Rebase uses for the target: a root starting with '/' is Unix.
        char separator = destinationRoot.StartsWith('/') ? '/' : '\\';
        string root = destinationRoot.TrimEnd('/', '\\');
        string flat = root + separator + PortablePath.GetFileName(source);

        string? owner = scanFolders
            .Where(f => !string.IsNullOrWhiteSpace(f) && FolderScope.IsUnder(source, f))
            .MaxBy(f => FolderScope.Normalize(f).Length);
        if (owner is null)
            return flat;

        // "M:\" has the leaf "M:", and ':' is not legal inside a Windows folder name.
        string leaf = PortablePath.GetFileName(owner.TrimEnd('/', '\\')).Replace(":", string.Empty);
        string target = leaf.Length == 0 ? root : root + separator + leaf;
        return PortablePath.Rebase(source, owner, target) ?? flat;
    }
}
