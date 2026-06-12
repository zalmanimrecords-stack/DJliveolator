namespace Liveolator.Core.Mapping;

/// <summary>
/// Exports/imports a <see cref="ControllerMappingProfile"/> to/from a user-chosen file (doc 05), so a
/// mapping authored for a device model can be shared between machines/installs. The on-disk format is the
/// same versioned JSON the live profile store uses, so an exported file can also be hand-edited or checked
/// in. A seam (impl in Media) keeps the App view-model free of file IO and testable with a fake.
/// </summary>
public interface IMappingProfilePortability
{
    /// <summary>Writes <paramref name="profile"/> as a versioned JSON document at <paramref name="filePath"/>.</summary>
    Task ExportAsync(ControllerMappingProfile profile, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Reads a profile from <paramref name="filePath"/>; null if missing, unreadable, or a wrong/old version.</summary>
    Task<ControllerMappingProfile?> ImportAsync(string filePath, CancellationToken cancellationToken = default);
}
