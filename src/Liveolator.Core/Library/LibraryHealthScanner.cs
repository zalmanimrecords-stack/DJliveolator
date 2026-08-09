using Liveolator.Core.Library.Doctor;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;

namespace Liveolator.Core.Library;

/// <summary>
/// The health-scan pipeline behind the Library Doctor: fill in the content hashes a duplicate check
/// needs, rebuild the media identities, persist them, and hand the catalog to <see cref="LibraryDoctor"/>.
/// Pure orchestration over seams — no UI, no busy state, no status text — so the expensive and
/// order-dependent part of a health scan is testable without a view-model (doc 16).
/// </summary>
/// <remarks>
/// Hashing is the costly step, so it is done only where it can change an answer: files that share a
/// byte size with another file are the only duplicate candidates, and a path that already carries a
/// stored hash is left alone.
/// </remarks>
public sealed class LibraryHealthScanner
{
    private readonly LibraryDoctor _doctor;
    private readonly IMediaIdentityStore? _identityStore;
    private readonly IFileContentHasher? _contentHasher;
    private readonly Func<DateTime> _utcNow;

    /// <param name="doctor">The health rules applied to the rebuilt catalog view.</param>
    /// <param name="identityStore">Loads and persists media identities; null skips both.</param>
    /// <param name="contentHasher">Computes SHA-256 for duplicate candidates; null skips hashing.</param>
    /// <param name="utcNow">Clock seam so identity timestamps are deterministic under test.</param>
    public LibraryHealthScanner(
        LibraryDoctor doctor,
        IMediaIdentityStore? identityStore = null,
        IFileContentHasher? contentHasher = null,
        Func<DateTime>? utcNow = null)
    {
        _doctor = doctor ?? throw new ArgumentNullException(nameof(doctor));
        _identityStore = identityStore;
        _contentHasher = contentHasher;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Runs one health scan and returns the report. Never mutates the catalog; the only write is the
    /// refreshed identity set, and only when an identity store was supplied.
    /// </summary>
    public async Task<LibraryDoctorReport> ScanAsync(
        IReadOnlyCollection<MusicTrack> tracks,
        IReadOnlyCollection<VisualAsset> visuals,
        IReadOnlyList<string> folders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(folders);

        Dictionary<string, string?> shaByPath = await LoadKnownHashesAsync(cancellationToken).ConfigureAwait(false);

        if (_contentHasher is not null)
            await FillDuplicateHashesAsync(
                tracks.Cast<IMediaEntry>().Concat(visuals),
                shaByPath,
                cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MediaIdentity> identities = MediaIdentityBuilder.FromCatalog(
            tracks, visuals, _utcNow(), shaByPath);

        if (_identityStore is not null)
            await _identityStore.SaveIdentitiesAsync(identities, cancellationToken).ConfigureAwait(false);

        return _doctor.Scan(tracks, visuals, folders, Array.Empty<string>(), identities);
    }

    // The hashes already recorded for each known path, so a rescan re-hashes only what it must. A path
    // recorded more than once keeps its most recent hash.
    private async Task<Dictionary<string, string?>> LoadKnownHashesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MediaIdentity> saved = _identityStore is null
            ? Array.Empty<MediaIdentity>()
            : await _identityStore.LoadIdentitiesAsync(cancellationToken).ConfigureAwait(false);

        return saved
            .SelectMany(identity => identity.Paths.Select(path => (Path: path, identity.Sha256)))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Sha256, StringComparer.OrdinalIgnoreCase);
    }

    // Hash only genuine duplicate candidates: entries sharing a byte size with a different path. A
    // unique size can never be a duplicate, so hashing it would be pure cost.
    private async Task FillDuplicateHashesAsync(
        IEnumerable<IMediaEntry> entries,
        Dictionary<string, string?> shaByPath,
        CancellationToken cancellationToken)
    {
        foreach (IGrouping<long, IMediaEntry> group in entries
                     .Where(e => e.File.SizeBytes > 0)
                     .GroupBy(e => e.File.SizeBytes)
                     .Where(g => g.Select(e => e.File.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            foreach (IMediaEntry entry in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (shaByPath.TryGetValue(entry.File.Path, out string? existing)
                    && !string.IsNullOrWhiteSpace(existing))
                    continue;

                shaByPath[entry.File.Path] = await _contentHasher!
                    .ComputeSha256Async(entry.File.Path, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
