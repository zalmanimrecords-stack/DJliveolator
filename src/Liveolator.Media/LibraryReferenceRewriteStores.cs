using Liveolator.Core.Library.Doctor;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist;
using Liveolator.Core.Visuals.TrackPrograms;

namespace Liveolator.Media;

public sealed class PlaylistReferenceRewriteStore : ILibraryReferenceRewriteStore
{
    private readonly IPlaylistStore _store;

    public PlaylistReferenceRewriteStore(IPlaylistStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Name => "playlists";

    public async Task<LibraryReferenceRewritePreview> PreviewAsync(
        IReadOnlyList<LibraryPathRewrite> rewrites,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> map = Map(rewrites);
        IReadOnlyList<string> names = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        int affected = 0;
        foreach (string name in names)
        {
            Playlist? playlist = await _store.LoadAsync(name, cancellationToken).ConfigureAwait(false);
            if (playlist is not null && playlist.TrackPaths.Any(path => map.ContainsKey(path)))
                affected++;
        }

        return new LibraryReferenceRewritePreview(affected, 0, 0, Array.Empty<string>());
    }

    public async Task ApplyAsync(IReadOnlyList<LibraryPathRewrite> rewrites, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> map = Map(rewrites);
        IReadOnlyList<string> names = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (string name in names)
        {
            Playlist? playlist = await _store.LoadAsync(name, cancellationToken).ConfigureAwait(false);
            if (playlist is null)
                continue;

            List<string> rewritten = playlist.TrackPaths
                .Select(path => map.TryGetValue(path, out string? replacement) ? replacement : path)
                .ToList();
            if (!rewritten.SequenceEqual(playlist.TrackPaths, StringComparer.OrdinalIgnoreCase))
                await _store.SaveAsync(playlist.WithTracks(rewritten), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> Map(IEnumerable<LibraryPathRewrite> rewrites)
        => rewrites.ToDictionary(r => r.OldPath, r => r.NewPath, StringComparer.OrdinalIgnoreCase);
}

public sealed class LiveSetReferenceRewriteStore : ILibraryReferenceRewriteStore
{
    private readonly ILiveSetStore _store;
    private readonly string _name;

    public LiveSetReferenceRewriteStore(string name, ILiveSetStore store)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "live set" : name;
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string Name => _name;

    public async Task<LibraryReferenceRewritePreview> PreviewAsync(
        IReadOnlyList<LibraryPathRewrite> rewrites,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> map = Map(rewrites);
        IReadOnlyList<string>? paths = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        int affected = paths is not null && paths.Any(path => map.ContainsKey(path)) ? 1 : 0;
        return new LibraryReferenceRewritePreview(0, affected, 0, Array.Empty<string>());
    }

    public async Task ApplyAsync(IReadOnlyList<LibraryPathRewrite> rewrites, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> map = Map(rewrites);
        IReadOnlyList<string>? paths = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (paths is null)
            return;

        List<string> rewritten = paths
            .Select(path => map.TryGetValue(path, out string? replacement) ? replacement : path)
            .ToList();
        if (!rewritten.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
            await _store.SaveAsync(rewritten, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string> Map(IEnumerable<LibraryPathRewrite> rewrites)
        => rewrites.ToDictionary(r => r.OldPath, r => r.NewPath, StringComparer.OrdinalIgnoreCase);
}

public sealed class TrackVisualProgramReferenceRewriteStore : ILibraryReferenceRewriteStore
{
    private readonly ITrackVisualProgramStore _store;

    public TrackVisualProgramReferenceRewriteStore(ITrackVisualProgramStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Name => "track visual programs";

    public async Task<LibraryReferenceRewritePreview> PreviewAsync(
        IReadOnlyList<LibraryPathRewrite> rewrites,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> map = Map(rewrites);
        IReadOnlyList<TrackVisualProgramSummary> summaries = await _store.ListAsync(cancellationToken)
            .ConfigureAwait(false);
        int affected = summaries.Count(s => map.ContainsKey(s.TrackPath));
        return new LibraryReferenceRewritePreview(0, 0, affected, Array.Empty<string>());
    }

    public async Task ApplyAsync(IReadOnlyList<LibraryPathRewrite> rewrites, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> map = Map(rewrites);
        IReadOnlyList<TrackVisualProgramSummary> summaries = await _store.ListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (TrackVisualProgramSummary summary in summaries)
        {
            if (!map.TryGetValue(summary.TrackPath, out string? replacement))
                continue;

            TrackVisualProgram? program = await _store.LoadAsync(summary.TrackPath, cancellationToken)
                .ConfigureAwait(false);
            if (program is null)
                continue;

            TrackReference track = program.Track with { Path = replacement };
            await _store.DeleteAsync(summary.TrackPath, cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(program with { Track = track }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> Map(IEnumerable<LibraryPathRewrite> rewrites)
        => rewrites.ToDictionary(r => r.OldPath, r => r.NewPath, StringComparer.OrdinalIgnoreCase);
}

