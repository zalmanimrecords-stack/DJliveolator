using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Enrichment;

namespace Liveolator.Core.Library.Music;

/// <summary>
/// Catalogs music files and runs offline BPM/key analysis on each (via <see cref="IAudioDecoder"/>
/// + <see cref="TrackAnalyzer"/>). Adds harmonic-mixing lookup over the catalog.
/// </summary>
public sealed class MusicLibrary : MediaLibrary<MusicTrack>
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aiff", ".aif", ".ogg", ".m4a", ".aac", ".wma", ".opus"
    };

    private readonly IAudioDecoder _decoder;
    private readonly TrackAnalyzer _analyzer;
    private readonly ITrackMetadataReader _metadataReader;
    private IReadOnlySet<string> _sampleFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public MusicLibrary(
        IFileEnumerator enumerator,
        IAudioDecoder decoder,
        TrackAnalyzer? analyzer = null,
        ITrackMetadataReader? metadataReader = null)
        : base(enumerator)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _analyzer = analyzer ?? new TrackAnalyzer();
        _metadataReader = metadataReader ?? NullTrackMetadataReader.Instance;
    }

    protected override IReadOnlySet<string> Extensions => AudioExtensions;

    protected override async Task<MusicTrack> CreateEntryAsync(ScannedFile file, CancellationToken cancellationToken)
    {
        TrackMetadata? metadata = ReadMetadata(file.Path);
        TrackAnalysisResult result = await _analyzer
            .AnalyzeAsync(_decoder, file.Path, cancellationToken)
            .ConfigureAwait(false);
        MediaAnalysisStatus status = TrackStatusPolicy.For(result);
        MusicMediaKind kind = SampleClassifier.Classify(file.Path, result.Duration, _sampleFolders);
        return CarryLibraryFields(
            new MusicTrack(
                file, result.Bpm, result.Key, result.Duration, result.Cues, status, null, metadata, kind,
                TrackAnalyzer.CurrentVersion) with { Structure = result.Structure },
            file.Path);
    }

    // A track that fails to decode can still have readable tags, so capture metadata here too. With no
    // duration it classifies as a Track unless the file sits under a designated samples folder.
    protected override MusicTrack CreateFailedEntry(ScannedFile file, string error)
        => CarryLibraryFields(
            new MusicTrack(file, null, null, null, TrackCues.None, MediaAnalysisStatus.Failed, error,
                ReadMetadata(file.Path), SampleClassifier.Classify(file.Path, null, _sampleFolders)),
            file.Path);

    // A rebuild (scan-modified or re-analyze) creates a fresh entry from the decoder and would drop the
    // user/library fields (rating, date-added, play history) — they are NOT analysis and must survive a
    // re-decode (global #7). Carry them from the still-catalogued prior entry; a genuinely new file has
    // no prior, so DateAdded is stamped now. (ForceReanalyze/online/manual edits use `existing with {…}`
    // and already keep these.)
    private MusicTrack CarryLibraryFields(MusicTrack rebuilt, string path)
    {
        MusicTrack? prior = TryGet(path);
        return rebuilt with
        {
            // Analysis just ran (this is the create/re-analyze path), so stamp "last scanned" now.
            LastAnalyzedUtc = DateTime.UtcNow,
            Rating = prior?.Rating ?? 0,
            DateAdded = prior?.DateAdded ?? DateTime.UtcNow,
            LastPlayed = prior?.LastPlayed,
            PlayCount = prior?.PlayCount ?? 0,
            // Online lookup data also survives a re-decode (or the next pass re-burns the free API),
            // but the conflict verdict is re-derived against the NEW local tempo so it stays honest.
            // A user-confirmed value stays confirmed — that decision belongs to the user, not the merge.
            OnlineBpm = prior?.OnlineBpm,
            OnlineBpmSource = prior?.OnlineBpmSource,
            OnlineLookupUtc = prior?.OnlineLookupUtc,
            BpmProvenance = prior?.BpmProvenance == BpmProvenance.LocalConfirmed
                ? BpmProvenance.LocalConfirmed
                : MetadataMergePolicy.MergeBpm(rebuilt.Bpm, prior?.OnlineBpm, rebuilt.Status).Provenance,
        };
    }

    // A user-locked beat grid / BPM / key (AnalysisIsManual) must survive a re-tag or any other file
    // change: rebuilding from the decoder would silently discard the DJ's manual correction (global
    // standard #7). Keep the manual entry as-is, only re-stamping the fingerprint to the new file so a
    // following scan sees it Unchanged instead of repeatedly trying to rebuild it.
    protected override MusicTrack? PreserveModifiedEntry(MusicTrack existing, ScannedFile file)
        => existing.AnalysisIsManual ? existing with { File = file } : null;

    // Relocation re-stamps the file (path + fingerprint) while keeping all analysis intact.
    protected override MusicTrack WithFile(MusicTrack entry, ScannedFile file)
        => entry with { File = file };

    /// <summary>
    /// Designates which scan folders hold samples (the classifier override) and **reclassifies the whole
    /// catalog in place** from cached durations — no re-decode, so toggling a folder is instant. New scans
    /// use the same set.
    /// </summary>
    public void SetSampleFolders(IEnumerable<string> sampleFolders)
    {
        ArgumentNullException.ThrowIfNull(sampleFolders);
        _sampleFolders = new HashSet<string>(
            sampleFolders.Where(f => !string.IsNullOrWhiteSpace(f)), StringComparer.OrdinalIgnoreCase);

        var reclassified = All
            .Select(t => t with { Kind = SampleClassifier.Classify(t.File.Path, t.Duration, _sampleFolders) })
            .ToList();
        Restore(reclassified);
    }

    /// <summary>Returns the catalogued entries of one kind (full tracks vs samples), mirroring VisualMediaLibrary.OfKind.</summary>
    public IReadOnlyList<MusicTrack> OfKind(MusicMediaKind kind)
        => All.Where(t => t.Kind == kind).ToList();

    /// <summary>
    /// A track still needs analysis when its decode/analysis failed or produced no tempo — e.g. the
    /// catalog was built before a working decoder was available (doc 16). These are the input to the
    /// background re-analysis pass.
    /// </summary>
    public static bool NeedsAnalysis(MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return !track.AnalysisIsManual
               && (track.Status == MediaAnalysisStatus.Failed
                   || track.Bpm is null
                   || track.AnalyzerVersion != TrackAnalyzer.CurrentVersion);
    }

    /// <summary>Applies a user-authored beat grid and protects it from automatic re-analysis.</summary>
    public bool SetManualBeatGrid(string path, double bpm, double firstBeatSeconds)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (bpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(bpm));
        if (firstBeatSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(firstBeatSeconds));

        MusicTrack? existing = TryGet(path);
        if (existing is null)
            return false;

        double confidence = existing.Bpm?.Confidence ?? 1.0;
        Upsert(existing with
        {
            Bpm = new Liveolator.Core.Analysis.Bpm.BpmResult(bpm, confidence, firstBeatSeconds),
            AnalyzerVersion = TrackAnalyzer.CurrentVersion,
            AnalysisIsManual = true,
        });
        return true;
    }

    /// <summary>
    /// Applies a user-corrected tempo and/or key to a catalogued track and locks the entry against
    /// automatic re-analysis (<see cref="MusicTrack.AnalysisIsManual"/>) — the escape hatch for a detector
    /// miss, where a confidently wrong value would otherwise stay in the catalog forever.
    /// <para>Whichever value is omitted keeps what analysis found, so a DJ can fix a tempo without
    /// asserting a key they have not checked. A corrected tempo keeps the existing beat anchor
    /// (<see cref="Bpm.BpmResult.FirstBeatSeconds"/> and the downbeat), because the grid's phase was
    /// usually right even when its rate was not. Returns the updated track, or null for an unknown path.</para>
    /// </summary>
    /// <exception cref="ArgumentException">Neither value was supplied, or the Camelot code is not 1A–12B.</exception>
    public MusicTrack? SetManualAnalysis(string path, double? bpm, string? camelot)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (bpm is null && string.IsNullOrWhiteSpace(camelot))
            throw new ArgumentException("Supply a BPM, a Camelot key, or both — there is nothing to correct otherwise.");
        if (bpm is <= 0)
            throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "BPM must be positive.");

        MusicalKey? key = null;
        if (!string.IsNullOrWhiteSpace(camelot) && !Camelot.TryToMusicalKey(camelot, out key))
            throw new ArgumentException("Camelot key must be between 1A and 12B.", nameof(camelot));

        MusicTrack? existing = TryGet(path);
        if (existing is null)
            return null;

        Liveolator.Core.Analysis.Bpm.BpmResult? corrected = bpm is { } value
            ? (existing.Bpm is { } prior
                ? prior with { Bpm = value, Confidence = 1.0 }
                : new Liveolator.Core.Analysis.Bpm.BpmResult(value, Confidence: 1.0))
            : existing.Bpm;
        MusicalKey? resolvedKey = key ?? existing.Key;

        // Only a track that now has BOTH a tempo and a key is fully analyzed; correcting one value on a
        // failed track does not make it mixable, so its status is left honest.
        bool complete = corrected is not null && resolvedKey is not null;
        MusicTrack updated = existing with
        {
            Bpm = corrected,
            Key = resolvedKey,
            Status = complete ? MediaAnalysisStatus.Ok : existing.Status,
            Error = complete ? null : existing.Error,
            AnalyzerVersion = TrackAnalyzer.CurrentVersion,
            AnalysisIsManual = true,
            // The DJ's value outranks the online cross-check, which cannot tell an extended mix from a
            // radio edit — so it is never re-flagged as conflicted.
            BpmProvenance = BpmProvenance.LocalConfirmed,
        };
        Upsert(updated);
        return updated;
    }

    /// <summary>
    /// Sets the user's 0–5 star rating on a catalogued track (0 clears it). Returns the updated track so
    /// the caller can persist just that one row, or null if the path isn't catalogued. Rating is user
    /// data — preserved across re-analysis.
    /// </summary>
    public MusicTrack? SetRating(string path, int rating)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (rating is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be 0–5.");

        MusicTrack? existing = TryGet(path);
        if (existing is null)
            return null;

        MusicTrack updated = existing with { Rating = rating };
        Upsert(updated);
        return updated;
    }

    /// <summary>
    /// Records that a track was loaded to a deck: bumps its play count and stamps the last-played time.
    /// Returns the updated track (so the caller can persist that one row), or null if the path — matched
    /// by exact path or file name (the same fallback the deck load uses) — isn't catalogued.
    /// </summary>
    public MusicTrack? MarkPlayed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        MusicTrack? existing = TryGetByPathOrName(path);
        if (existing is null)
            return null;

        MusicTrack updated = existing with { PlayCount = existing.PlayCount + 1, LastPlayed = DateTime.UtcNow };
        Upsert(updated);
        return updated;
    }

    /// <summary>Paths of the catalogued tracks that still need analysis (Failed / no BPM).</summary>
    public IReadOnlyList<string> PathsNeedingAnalysis()
        => All.Where(NeedsAnalysis).Select(t => t.File.Path).ToList();

    /// <summary>
    /// Paths of every catalogued track eligible for a full re-map ("Rescan all") — all tracks except
    /// those the user has manually corrected (<see cref="MusicTrack.AnalysisIsManual"/>), whose hand-set
    /// grid must survive (global #7). Unlike <see cref="PathsNeedingAnalysis"/>, this includes
    /// already-analyzed tracks, so a forced pass re-decodes them.
    /// </summary>
    public IReadOnlyList<string> PathsForFullRemap()
        => All.Where(t => !t.AnalysisIsManual).Select(t => t.File.Path).ToList();

    /// <summary>
    /// Re-runs offline analysis for one already-catalogued track (e.g. one previously Failed because no
    /// decoder was available) and replaces its entry in place. Returns true when the track is now
    /// analyzed. An unknown or already-analyzed path is a no-op (returns false) so a good track is never
    /// re-decoded; a decode failure is captured as a Failed entry rather than thrown (global #16/#26).
    /// </summary>
    public async Task<bool> ReanalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        MusicTrack? existing = TryGet(path);
        if (existing is null || !NeedsAnalysis(existing))
            return false;

        MusicTrack rebuilt;
        try
        {
            rebuilt = await CreateEntryAsync(existing.File, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            rebuilt = CreateFailedEntry(existing.File, ex.Message);
        }

        Upsert(rebuilt);
        return !NeedsAnalysis(rebuilt);
    }

    public async Task<bool> ForceReanalyzeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        MusicTrack? existing = TryGet(path);
        if (existing is null)
            return false;

        try
        {
            TrackAnalysisResult result = await _analyzer
                .AnalyzeAsync(_decoder, path, cancellationToken)
                .ConfigureAwait(false);
            Upsert(existing with
            {
                Bpm = result.Bpm,
                Key = result.Key,
                Duration = result.Duration,
                Cues = result.Cues,
                Structure = result.Structure,
                Status = TrackStatusPolicy.For(result),
                Error = null,
                AnalyzerVersion = TrackAnalyzer.CurrentVersion,
                AnalysisIsManual = false,
                LastAnalyzedUtc = DateTime.UtcNow,
            });
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Upsert(existing with
            {
                Status = MediaAnalysisStatus.Failed, Error = ex.Message, LastAnalyzedUtc = DateTime.UtcNow,
            });
            return false;
        }
    }

    public bool UpdateManualDetails(
        string path,
        double bpm,
        string camelot,
        string? genre,
        string? comment)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (bpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(bpm));
        if (!Camelot.TryToMusicalKey(camelot, out MusicalKey? key))
            throw new ArgumentException("Camelot key must be between 1A and 12B.", nameof(camelot));

        MusicTrack? existing = TryGet(path);
        if (existing is null)
            return false;

        TrackMetadata metadata = (existing.Metadata ?? TrackMetadata.Empty) with
        {
            Genre = Normalize(genre),
            Comment = Normalize(comment),
        };
        Upsert(existing with
        {
            Bpm = new Liveolator.Core.Analysis.Bpm.BpmResult(
                bpm, Confidence: 1.0, existing.Bpm?.FirstBeatSeconds ?? 0),
            Key = key,
            Metadata = metadata,
            Status = MediaAnalysisStatus.Ok,
            Error = null,
            AnalyzerVersion = TrackAnalyzer.CurrentVersion,
            AnalysisIsManual = true,
        });
        return true;
    }

    public bool ApplyOnlineDetails(string path, OnlineTrackMetadata online)
    {
        MusicTrack? existing = TryGet(path);
        if (existing is null)
            return false;

        EnrichedBpm enriched =
            MetadataMergePolicy.MergeBpm(existing.Bpm, online.Bpm, existing.Status);
        // Preserve the existing analysis record (FirstBeatSeconds, DownbeatSeconds, BeatsPerBar, …) and
        // only override the value/confidence — rebuilding from the positional ctor silently dropped the
        // v4 downbeat anchor on a cross-check of an already-analyzed track (doc 31 L1).
        Liveolator.Core.Analysis.Bpm.BpmResult? bpm = enriched.Bpm is { } value
            ? (existing.Bpm is { } prior
                ? prior with { Bpm = value, Confidence = enriched.Confidence }
                : new Liveolator.Core.Analysis.Bpm.BpmResult(value, enriched.Confidence))
            : existing.Bpm;

        // Fill/replace a missing-or-weak key from the online result. Prefer an explicit Camelot code;
        // fall back to parsing a key NAME (e.g. GetSongBPM reports "Am", never a Camelot code — without
        // this fallback the online key was silently discarded, doc 27 B7).
        MusicalKey? key = existing.Key;
        if (key is null || key.Confidence < 0.2)
        {
            if (Camelot.TryToMusicalKey(online.Camelot, out MusicalKey? fromCamelot))
                key = fromCamelot;
            else if (KeyName.TryParse(online.KeyName, out MusicalKey? fromName))
                key = fromName;
        }

        // When enrichment produced a usable tempo and lifted the track off Failed, stamp the current
        // analyzer version so NeedsAnalysis stops flagging it — otherwise an enriched previously-Failed
        // track churns in the pending queue forever and a later re-analysis overwrites the online data
        // (doc 31 L2). A still-Failed/tempo-less result is left eligible.
        bool enrichmentIsUsable = bpm is not null && enriched.Status != MediaAnalysisStatus.Failed;

        // A manually-set or user-confirmed local BPM is never re-flagged: the user's decision outranks
        // the online cross-check (which can't tell an extended mix from the radio edit).
        BpmProvenance provenance = enriched.Provenance;
        MediaAnalysisStatus status = enriched.Status;
        if (existing.AnalysisIsManual || existing.BpmProvenance == BpmProvenance.LocalConfirmed)
        {
            if (provenance == BpmProvenance.Conflicted)
                status = existing.Status;
            provenance = BpmProvenance.LocalConfirmed;
        }

        Upsert(existing with
        {
            Bpm = bpm,
            Key = key,
            Status = status,
            AnalyzerVersion = enrichmentIsUsable ? TrackAnalyzer.CurrentVersion : existing.AnalyzerVersion,
            Metadata = string.IsNullOrWhiteSpace(online.Genre)
                ? existing.Metadata
                : (existing.Metadata ?? TrackMetadata.Empty) with { Genre = online.Genre.Trim() },
            OnlineBpm = online.Bpm ?? existing.OnlineBpm,
            OnlineBpmSource = online.Bpm is null ? existing.OnlineBpmSource : online.Source,
            BpmProvenance = provenance,
        });
        return true;
    }

    /// <summary>
    /// Stamps that an online lookup COMPLETED for this track (hit or miss), so the next enrichment
    /// pass skips it instead of re-querying the courtesy-free API. Returns false for an unknown path.
    /// </summary>
    public bool MarkOnlineLookup(string path, DateTime lookedUpUtc)
    {
        MusicTrack? existing = TryGet(path);
        if (existing is null)
            return false;

        Upsert(existing with { OnlineLookupUtc = lookedUpUtc });
        return true;
    }

    /// <summary>
    /// User resolution for a BPM conflict: keep the locally detected value and stop flagging the track.
    /// Returns true only when the track existed and was actually conflicted.
    /// </summary>
    public bool ConfirmLocalBpm(string path)
    {
        MusicTrack? existing = TryGet(path);
        if (existing is null || existing.BpmProvenance != BpmProvenance.Conflicted)
            return false;

        Upsert(existing with { BpmProvenance = BpmProvenance.LocalConfirmed });
        return true;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The reader contract is "never throws", but guard anyway so a misbehaving reader
    // can never abort a scan — metadata simply degrades to null.
    private TrackMetadata? ReadMetadata(string path)
    {
        try
        {
            return _metadataReader.Read(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns successfully-analyzed tracks whose key is a harmonically-compatible mix from
    /// <paramref name="seed"/> (Camelot rules), excluding the seed itself.
    /// </summary>
    public IReadOnlyList<MusicTrack> HarmonicMatches(MusicTrack seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (seed.Key is null)
            return Array.Empty<MusicTrack>();

        // A confident key is enough for harmonic mixing even if the tempo was uncertain,
        // so include PartiallyAnalyzed tracks — exclude only Failed (no key) and the seed.
        return All
            .Where(t => t.Status != MediaAnalysisStatus.Failed
                        && t.Key is not null
                        && !string.Equals(t.File.Path, seed.File.Path, StringComparison.OrdinalIgnoreCase)
                        && Camelot.IsCompatible(seed.Key.Camelot, t.Key.Camelot))
            .ToList();
    }

    /// <summary>
    /// Rolls up the current catalog per folder root: for each folder, the count of catalogued
    /// tracks whose file lives under it and the Ok / PartiallyAnalyzed / Failed breakdown. One
    /// summary is returned per input folder, in order; a folder with no catalogued tracks yields
    /// an all-zero summary. Tracks outside every folder are counted in none.
    /// </summary>
    public IReadOnlyList<FolderCatalogSummary> SummarizeFolders(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        IReadOnlyCollection<MusicTrack> all = All;
        var summaries = new List<FolderCatalogSummary>();

        foreach (string folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder))
                continue;

            string root = FolderScope.Normalize(folder);
            int total = 0, ok = 0, partial = 0, failed = 0;

            foreach (MusicTrack track in all)
            {
                if (!FolderScope.IsUnderNormalized(FolderScope.Normalize(track.File.Path), root))
                    continue;

                total++;
                switch (track.Status)
                {
                    case MediaAnalysisStatus.Ok: ok++; break;
                    case MediaAnalysisStatus.PartiallyAnalyzed: partial++; break;
                    default: failed++; break;
                }
            }

            summaries.Add(new FolderCatalogSummary(folder, total, ok, partial, failed));
        }

        return summaries;
    }
}
