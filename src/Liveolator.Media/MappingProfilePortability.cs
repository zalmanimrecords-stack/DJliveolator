using Liveolator.Core.Mapping;

namespace Liveolator.Media;

/// <summary>
/// Media binding for <see cref="IMappingProfilePortability"/> (doc 05): exports/imports a controller
/// mapping profile to/from an arbitrary file path using the same versioned <see cref="MappingProfileSnapshot"/>
/// JSON the <see cref="LiveProfileStore"/> writes, so exported maps interoperate with the live store and a
/// wrong/old version is rejected rather than mis-loaded. Tolerant: a missing/corrupt file imports as null.
/// </summary>
public sealed class MappingProfilePortability : IMappingProfilePortability
{
    private readonly JsonFileSnapshotIo _io;

    public MappingProfilePortability(Action<string>? onWarning = null)
        => _io = new JsonFileSnapshotIo(onWarning);

    public Task ExportAsync(ControllerMappingProfile profile, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return _io.SaveAsync(
            filePath,
            new MappingProfileSnapshot(MappingProfileSnapshot.CurrentVersion, profile),
            cancellationToken);
    }

    public async Task<ControllerMappingProfile?> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        MappingProfileSnapshot? snapshot = await _io.LoadAsync<MappingProfileSnapshot>(filePath, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        if (snapshot.Version != MappingProfileSnapshot.CurrentVersion)
        {
            _io.WarnVersionMismatch(filePath, snapshot.Version, MappingProfileSnapshot.CurrentVersion);
            return null;
        }
        return snapshot.Profile;
    }
}
