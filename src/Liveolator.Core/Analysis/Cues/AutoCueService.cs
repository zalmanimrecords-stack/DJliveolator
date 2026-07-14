using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Core.Analysis.Structure;
using Liveolator.Core.Persistence;

namespace Liveolator.Core.Analysis.Cues;

/// <summary>Progress of a background auto-cue pass.</summary>
/// <param name="Done">Tracks processed so far.</param>
/// <param name="Total">Tracks in the batch.</param>
/// <param name="Cued">Of those processed, how many had auto cues written.</param>
public readonly record struct AutoCueProgress(int Done, int Total, int Cued);

/// <summary>Result of a completed (or cancelled) auto-cue pass.</summary>
/// <param name="Considered">Tracks in the batch.</param>
/// <param name="Cued">How many had auto cues written.</param>
public readonly record struct AutoCueOutcome(int Considered, int Cued);

/// <summary>
/// Runs automatic hot-cue placement over a batch of tracks and persists the results (doc 11/16). For each
/// track it decodes + analyzes (via <see cref="AutoCueAnalyzer"/>), merges the suggested cues into the
/// track's existing stored cues preserving the DJ's manual cues (via <see cref="AutoCueMerger"/>), and
/// saves to the <see cref="IHotCueStore"/> so the cues light up on the next deck load.
/// </summary>
/// <remarks>
/// Pure orchestration — no UI, no native, no audio-thread work: it only touches the offline decoder and
/// the JSON cue store, exactly like <c>CatalogReanalysisService</c>. Intended for a background thread.
/// A single track's decode/analysis failure is reported and never stops the pass (global standards
/// #16/#26); cancellation is honoured between tracks. Which tracks to pass — and not running while a deck
/// is actively playing — is the caller's policy.
/// </remarks>
public sealed class AutoCueService : IAutoCueService
{
    private readonly AutoCueAnalyzer _analyzer;
    private readonly AutoCueMerger _merger;
    private readonly IAudioDecoder _decoder;
    private readonly IHotCueStore _store;
    private readonly Func<string, SongStructure?>? _structureProvider;
    private readonly Action<string>? _onError;

    /// <param name="decoder">Offline decoder used to read each track's PCM.</param>
    /// <param name="store">Persistent per-track cue store the merged cues are written to.</param>
    /// <param name="analyzer">Auto-cue analyzer; defaults to a standard instance.</param>
    /// <param name="merger">Manual-preserving merger; defaults to a standard instance.</param>
    /// <param name="structureProvider">Looks up a track's already-computed <see cref="SongStructure"/> (from
    /// the catalog / offline Python analysis, doc 32) by path so cues anchor on real section boundaries. When
    /// null or it returns null, the analyzer falls back to its heuristic structural detector.</param>
    /// <param name="onError">Receives a note for a single track failure; the pass continues.</param>
    public AutoCueService(
        IAudioDecoder decoder,
        IHotCueStore store,
        AutoCueAnalyzer? analyzer = null,
        AutoCueMerger? merger = null,
        Func<string, SongStructure?>? structureProvider = null,
        Action<string>? onError = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _analyzer = analyzer ?? new AutoCueAnalyzer();
        _merger = merger ?? new AutoCueMerger();
        _structureProvider = structureProvider;
        _onError = onError;
    }

    /// <summary>
    /// Places and persists auto cues for every track in <paramref name="trackPaths"/>. Tracks the decoder
    /// cannot handle, that fail to decode, or whose tempo is undetectable are skipped (not cued) without
    /// stopping the pass. Honours cancellation between tracks.
    /// </summary>
    public async Task<AutoCueOutcome> RunAsync(
        IReadOnlyList<string> trackPaths,
        IProgress<AutoCueProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackPaths);

        int total = trackPaths.Count;
        int done = 0, cued = 0;

        foreach (string path in trackPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await ProcessAsync(path, cancellationToken).ConfigureAwait(false))
                    cued++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _onError?.Invoke($"Auto-cue analysis of '{path}' failed: {ex.Message}");
            }

            done++;
            progress?.Report(new AutoCueProgress(done, total, cued));
        }

        return new AutoCueOutcome(total, cued);
    }

    /// <summary>Processes one track; returns true when auto cues were written.</summary>
    private async Task<bool> ProcessAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !_decoder.CanDecode(path))
            return false;

        SongStructure? structure = _structureProvider?.Invoke(path);
        TrackCueSet? auto = await _analyzer
            .AnalyzeAsync(_decoder, path, structure, cancellationToken)
            .ConfigureAwait(false);
        if (auto is null || auto.HotCues.Count == 0)
            return false;

        TrackCueRecord? existingRecord = await _store.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        TrackCueSet merged = _merger.Merge(existingRecord?.ToCueSet(), auto);

        await _store.SaveAsync(TrackCueRecord.FromCueSet(path, merged), cancellationToken).ConfigureAwait(false);
        return true;
    }
}
