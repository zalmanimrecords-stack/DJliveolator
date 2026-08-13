using Liveolator.Core.Analysis;
using Liveolator.Core.Persistence;

namespace Liveolator.Core.Library.Music;

/// <summary>Progress of a loudness-measuring pass.</summary>
/// <param name="Done">Tracks processed so far.</param>
/// <param name="Total">Tracks that lacked a measurement when the pass started.</param>
/// <param name="Measured">Of those processed, how many now carry a loudness value.</param>
public readonly record struct LoudnessProgress(int Done, int Total, int Measured);

/// <summary>Result of a completed (or cancelled) loudness pass.</summary>
/// <param name="Considered">Tracks that lacked a measurement when the pass started.</param>
/// <param name="Measured">How many were successfully measured.</param>
public readonly record struct LoudnessOutcome(int Considered, int Measured);

/// <summary>
/// Fills in the integrated loudness of catalogued tracks that lack it, so a DJ set can gain every clip to
/// one level instead of playing each master at unity.
/// <para>Deliberately its own pass rather than part of <see cref="TrackAnalyzer"/>: loudness is independent
/// of tempo, key and structure, so folding it into the analyzer would force a full re-analysis of the whole
/// catalog for a number none of those detectors produce — and because a bumped analyzer version skips
/// hand-corrected tracks, it would skip exactly the ones a curated set is built from.</para>
/// <para>Persists incrementally, so the pass is resumable across restarts and a long run on a large catalog
/// never loses more than <c>persistEvery</c> tracks of work.</para>
/// </summary>
public sealed class CatalogLoudnessService
{
    private readonly MusicLibrary _library;
    private readonly ILoudnessMeter _meter;
    private readonly IMusicCatalogStore? _store;
    private readonly int _persistEvery;
    private readonly Action<string>? _onError;

    /// <param name="library">The catalog to measure in place.</param>
    /// <param name="meter">Measures one file; a null result means "could not measure", not a failure.</param>
    /// <param name="store">Persists the updated catalog; null skips persistence (in-memory only).</param>
    /// <param name="persistEvery">Save every N processed tracks, bounding work lost to a crash.</param>
    /// <param name="onError">Receives a note for a single track/persist failure; the pass continues.</param>
    public CatalogLoudnessService(
        MusicLibrary library,
        ILoudnessMeter meter,
        IMusicCatalogStore? store = null,
        int persistEvery = 25,
        Action<string>? onError = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        _store = store;
        _persistEvery = persistEvery > 0 ? persistEvery : 1;
        _onError = onError;
    }

    /// <summary>
    /// Measures every track that currently lacks a loudness value, reporting progress and persisting
    /// periodically. One track's failure is recorded and never stops the pass. Honours cancellation between
    /// tracks; on cancel it persists the work already done before rethrowing, so the next run resumes.
    /// </summary>
    public async Task<LoudnessOutcome> RunAsync(
        IProgress<LoudnessProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> pending = _library.PathsNeedingLoudness();
        int total = pending.Count;
        if (total == 0)
        {
            progress?.Report(new LoudnessProgress(0, 0, 0));
            return new LoudnessOutcome(0, 0);
        }

        int done = 0, measured = 0;
        bool dirtySincePersist = false;
        try
        {
            foreach (string path in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    double? lufs = await _meter
                        .MeasureIntegratedLufsAsync(path, cancellationToken).ConfigureAwait(false);
                    if (_library.SetLoudness(path, lufs))
                        dirtySincePersist = true;
                    if (lufs is not null)
                        measured++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _onError?.Invoke($"Measuring the loudness of '{path}' failed: {ex.Message}");
                }

                done++;
                progress?.Report(new LoudnessProgress(done, total, measured));

                if (dirtySincePersist && done % _persistEvery == 0)
                {
                    await PersistAsync().ConfigureAwait(false);
                    dirtySincePersist = false;
                }
            }
        }
        finally
        {
            // Always flush remaining progress (including on cancellation) so the measuring just done is not
            // lost. Uses no token so a cancel cannot also abort this save.
            if (dirtySincePersist)
                await PersistAsync().ConfigureAwait(false);
        }

        return new LoudnessOutcome(total, measured);
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
            _onError?.Invoke($"Saving the measured catalog failed: {ex.Message}");
        }
    }
}
