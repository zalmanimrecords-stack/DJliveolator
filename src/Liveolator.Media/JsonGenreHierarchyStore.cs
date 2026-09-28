using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;

namespace Liveolator.Media;

/// <summary>Versioned on-disk shape of the persisted genre hierarchy.</summary>
public sealed record GenreHierarchySnapshot(int Version, Dictionary<string, string> ParentByGenre)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists the user-curated genre hierarchy as its own JSON file under the per-user app-data root, so
/// a DJ's genre grouping (e.g. "Deep House" under "House") reloads on the next run. Held separately from
/// the music catalog and the cue store so editing the hierarchy never invalidates either
/// (backward-compatible by construction — global standards #20/#22).
/// </summary>
/// <remarks>
/// A missing or corrupt file yields an empty tree (every genre top-level, today's behavior) and reports
/// a warning — it never crashes the app (global standards #16/#26). Saves are atomic (temp-then-move)
/// via the shared <see cref="JsonFileSnapshotIo"/>, mirroring <see cref="JsonHotCueStore"/>.
/// </remarks>
public sealed class JsonGenreHierarchyStore : IGenreHierarchyStore
{
    private readonly string _directory;
    private readonly JsonFileSnapshotIo _io;

    public JsonGenreHierarchyStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _directory = rootDirectory ?? DefaultRoot();
        _io = new JsonFileSnapshotIo(onWarning);
    }

    /// <summary>Full path of the genre hierarchy JSON file.</summary>
    public string GenreHierarchyPath => Path.Combine(_directory, "genre-hierarchy.json");

    /// <summary>Default persistence root: <c>%APPDATA%/Liveolator</c> (or the XDG/Mac equivalent).</summary>
    public static string DefaultRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liveolator");

    public async Task<GenreHierarchy> LoadAsync(CancellationToken cancellationToken = default)
    {
        GenreHierarchySnapshot? snapshot =
            await _io.LoadAsync<GenreHierarchySnapshot>(GenreHierarchyPath, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new GenreHierarchy();

        if (snapshot.Version != GenreHierarchySnapshot.CurrentVersion)
        {
            _io.WarnVersionMismatch(GenreHierarchyPath, snapshot.Version, GenreHierarchySnapshot.CurrentVersion);
            return new GenreHierarchy();
        }

        return GenreHierarchy.FromMappings(snapshot.ParentByGenre ?? new Dictionary<string, string>());
    }

    public Task SaveAsync(GenreHierarchy hierarchy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        var snapshot = new GenreHierarchySnapshot(
            GenreHierarchySnapshot.CurrentVersion,
            new Dictionary<string, string>(hierarchy.Mappings, StringComparer.OrdinalIgnoreCase));
        return _io.SaveAsync(GenreHierarchyPath, snapshot, cancellationToken);
    }
}
