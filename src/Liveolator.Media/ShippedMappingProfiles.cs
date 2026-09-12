using Liveolator.Core.Mapping;

namespace Liveolator.Media;

/// <summary>
/// The controller mapping profiles that ship *with* the application: every <c>&lt;name&gt;.json</c>
/// in a folder, in the same versioned <see cref="MappingProfileSnapshot"/> shape
/// <see cref="MappingProfilePortability"/> exports and <see cref="LiveProfileStore"/> saves. Adding a
/// controller is therefore a data file, not a class — and a profile a performer learned and exported
/// can be shipped, or contributed, unchanged (doc 05).
/// </summary>
/// <remarks>
/// Synchronous on purpose: the composition root builds the profile catalog before there is a UI
/// thread to await on, and a sync-over-async wait on that path is what froze this app once (doc 27).
/// Tolerant by design: one unreadable, wrong-version or profile-less file is warned about and
/// skipped, never thrown, so a bad drop-in cannot stop the app from starting (global standards
/// #16/#26).
/// </remarks>
public static class ShippedMappingProfiles
{
    /// <summary>Folder, under the application directory, that holds the shipped profiles.</summary>
    public const string FolderName = "mappings";

    /// <summary>
    /// Loads every readable profile in <paramref name="directory"/>, ordered by file name so the
    /// catalog — and with it <see cref="MidiProfileSelector"/>'s first-match tie-break — is
    /// deterministic across machines. A missing directory yields an empty list.
    /// </summary>
    public static IReadOnlyList<ControllerMappingProfile> LoadFrom(
        string directory, Action<string>? onWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
            return Array.Empty<ControllerMappingProfile>();

        var io = new JsonFileSnapshotIo(onWarning);
        var profiles = new List<ControllerMappingProfile>();

        foreach (string path in Directory
                     .EnumerateFiles(directory, "*.json")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            MappingProfileSnapshot? snapshot = io.Load<MappingProfileSnapshot>(path);
            if (snapshot is null)
                continue;

            if (snapshot.Version != MappingProfileSnapshot.CurrentVersion)
            {
                io.WarnVersionMismatch(path, snapshot.Version, MappingProfileSnapshot.CurrentVersion);
                continue;
            }

            // Well-formed JSON carrying no profile deserializes to a snapshot with a null Profile;
            // adding it would put a null in the catalog the selector then walks.
            if (snapshot.Profile is null)
            {
                onWarning?.Invoke($"Mapping profile at '{path}' contains no profile; ignoring it.");
                continue;
            }

            profiles.Add(snapshot.Profile);
        }

        return profiles;
    }
}
