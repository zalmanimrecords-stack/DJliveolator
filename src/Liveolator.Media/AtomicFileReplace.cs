namespace Liveolator.Media;

/// <summary>
/// The final step of every store's save: move a fully written temp file over the live one.
/// </summary>
/// <remarks>
/// <para>
/// Writing to a unique temp and moving it is what makes a save atomic — a reader sees the old file or
/// the new one, never half of either. The move itself, however, is not immune to company: on Windows
/// <c>File.Move(..., overwrite: true)</c> is <c>MoveFileEx(MOVEFILE_REPLACE_EXISTING)</c>, and while
/// one writer is swapping the destination a second one gets <see cref="UnauthorizedAccessException"/>
/// or a sharing violation. Anti-virus and search indexers open files behind your back and cause the
/// same thing.
/// </para>
/// <para>
/// That collision is transient — measured in microseconds — but its consequence is not: the save
/// throws, and the data the user just changed is gone. This matters here because the app and the
/// <c>liveolator-mcp</c> server write the same playlist and studio-project files, and because
/// per-instance save gates cannot serialize across processes.
/// </para>
/// <para>
/// So the replace retries over a short budget and then gives up with the original exception, because
/// a destination that is locked for good is a real failure the store must still surface (global
/// standards #26 — never fail silently).
/// </para>
/// </remarks>
internal static class AtomicFileReplace
{
    // Six attempts over ~310 ms: long enough to outlast another writer's swap or a scanner's handle,
    // short enough that a genuinely stuck file still reports quickly.
    private static readonly int[] BackoffMilliseconds = [10, 20, 40, 80, 160];

    /// <summary>
    /// Moves <paramref name="tempPath"/> onto <paramref name="destinationPath"/>, replacing it,
    /// retrying briefly while the destination is held by someone else.
    /// </summary>
    public static async Task ReplaceAsync(
        string tempPath, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(tempPath, destinationPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (
                (ex is UnauthorizedAccessException or IOException) && attempt < BackoffMilliseconds.Length)
            {
                // Deliberately not logged: under contention this is the normal path and succeeds on the
                // next attempt. The throw below is what a caller needs to hear about.
                await Task.Delay(BackoffMilliseconds[attempt], cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
