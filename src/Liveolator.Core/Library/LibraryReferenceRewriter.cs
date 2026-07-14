namespace Liveolator.Core.Library.Doctor;

public sealed record LibraryPathRewrite(string OldPath, string NewPath);

public sealed record LibraryReferenceRewritePreview(
    int PlaylistsAffected,
    int LiveSetsAffected,
    int VisualLinksAffected,
    IReadOnlyList<string> Blockers);

public interface ILibraryReferenceRewriteStore
{
    string Name { get; }

    Task<LibraryReferenceRewritePreview> PreviewAsync(
        IReadOnlyList<LibraryPathRewrite> rewrites,
        CancellationToken cancellationToken = default);

    Task ApplyAsync(
        IReadOnlyList<LibraryPathRewrite> rewrites,
        CancellationToken cancellationToken = default);
}

public sealed class LibraryReferenceRewriter
{
    private readonly IReadOnlyList<ILibraryReferenceRewriteStore> _stores;

    public LibraryReferenceRewriter(IEnumerable<ILibraryReferenceRewriteStore> stores)
        => _stores = stores?.ToList() ?? throw new ArgumentNullException(nameof(stores));

    public async Task<LibraryReferenceRewritePreview> PreviewAsync(
        IReadOnlyList<LibraryPathRewrite> rewrites,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rewrites);
        if (rewrites.Count == 0)
            return new LibraryReferenceRewritePreview(0, 0, 0, Array.Empty<string>());

        if (_stores.Count == 0)
            return new LibraryReferenceRewritePreview(
                0, 0, 0,
                new[] { "No authored-data reference rewriters are registered; catalog path rewrite is blocked." });

        int playlists = 0, liveSets = 0, visuals = 0;
        var blockers = new List<string>();
        foreach (ILibraryReferenceRewriteStore store in _stores)
        {
            LibraryReferenceRewritePreview preview = await store.PreviewAsync(rewrites, cancellationToken)
                .ConfigureAwait(false);
            playlists += preview.PlaylistsAffected;
            liveSets += preview.LiveSetsAffected;
            visuals += preview.VisualLinksAffected;
            blockers.AddRange(preview.Blockers);
        }

        return new LibraryReferenceRewritePreview(playlists, liveSets, visuals, blockers);
    }

    public async Task ApplyAsync(
        IReadOnlyList<LibraryPathRewrite> rewrites,
        CancellationToken cancellationToken = default)
    {
        LibraryReferenceRewritePreview preview = await PreviewAsync(rewrites, cancellationToken)
            .ConfigureAwait(false);
        if (preview.Blockers.Count > 0)
            throw new InvalidOperationException(string.Join(" ", preview.Blockers));

        foreach (ILibraryReferenceRewriteStore store in _stores)
            await store.ApplyAsync(rewrites, cancellationToken).ConfigureAwait(false);
    }
}

