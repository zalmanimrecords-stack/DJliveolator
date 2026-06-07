using Liveolator.Core.Persistence;

namespace Liveolator.Core.Library.Music;

/// <summary>Progress of a background re-analysis pass.</summary>
/// <param name="Done">Tracks processed so far.</param>
/// <param name="Total">Tracks that needed analysis when the pass started.</param>
/// <param name="Analyzed">Of those processed, how many now have analysis (the rest failed to decode).</param>
public readonly record struct ReanalysisProgress(int Done, int Total, int Analyzed);

/// <summary>Result of a completed (or cancelled) re-analysis pass.</summary>
/// <param name="Considered">Tracks that needed analysis when the pass started.</param>
/// <param name="Analyzed">How many were successfully analyzed.</param>
public readonly record struct ReanalysisOutcome(int Considered, int Analyzed);

/// <summary>
/// Re-analyzes catalogued tracks that still lack analysis (Failed / no BPM) — typically a catalog built
/// before a working decoder was present (doc 16). Intended to run on a background thread at startup so
/// the app comes up immediately and BPM/key fill in progressively; it persists incrementally, so it is
/// resumable across restarts (already-analyzed tracks are skipped). Pure orchestration over
/// <see cref="MusicLibrary"/> (which owns the decode/analysis) and the catalog store — no UI, no native.
/// </summary>
public sealed class CatalogReanalysisService
{
    private readonly MusicLibrary _library;
    private readonly IMusicCatalogStore? _store;
    private readonly int _persistEvery;
    private readonly Action<string>? _onError;

    /// <param name="library">The catalog to re-analyze in place.</param>
    /// <param name="store">Persists the updated catalog; null skips persistence (in-memory only).</param>
    /// <param name="persistEvery">Save the catalog every N processed tracks, bounding lost work on a crash.</param>
    /// <param name="onError">Receives a note for a single track/persist failure; the pass continues.</param>
    public CatalogReanalysisService(
        MusicLibrary library, IMusicCatalogStore? store = null, int persistEvery = 25, Action<string>? onError = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store;
        _persistEvery = persistEvery > 0 ? persistEvery : 1;
        _onError = onError;
    }

    /// <summary>
    /// Analyzes every track that currently needs it, reporting progress and persisting periodically.
    /// One track's decode failure is recorded (as a still-Failed entry) and never stops the pass.
    /// Honours cancellation between tracks; on cancel it persists progress already made before rethrowing,
    /// so the next run resumes rather than repeats.
    /// </summary>
    public async Task<ReanalysisOutcome> RunAsync(
        IProgress<ReanalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> pending = _library.PathsNeedingAnalysis();
        int total = pending.Count;
        if (total == 0)
        {
            progress?.Report(new ReanalysisProgress(0, 0, 0));
            return new ReanalysisOutcome(0, 0);
        }

        int done = 0, analyzed = 0;
        bool dirtySincePersist = false;
        try
        {
            foreach (string path in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await _library.ReanalyzeAsync(path, cancellationToken).ConfigureAwait(false))
                        analyzed++;
                    dirtySincePersist = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _onError?.Invoke($"Re-analysis of '{path}' failed: {ex.Message}");
                }

                done++;
                progress?.Report(new ReanalysisProgress(done, total, analyzed));

                if (dirtySincePersist && done % _persistEvery == 0)
                {
                    await PersistAsync().ConfigureAwait(false);
                    dirtySincePersist = false;
                }
            }
        }
        finally
        {
            // Always flush remaining progress (including on cancellation) so the work just done is not
            // lost — the run is resumable. Uses no token so a cancel can't also abort this save.
            if (dirtySincePersist)
                await PersistAsync().ConfigureAwait(false);
        }

        return new ReanalysisOutcome(total, analyzed);
    }

    private async Task PersistAsync()
    {
        if (_store is null)
            return;
        try
        {
            await _store.SaveMusicAsync(_library.All, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _onError?.Invoke($"Saving the re-analyzed catalog failed: {ex.Message}");
        }
    }
}
