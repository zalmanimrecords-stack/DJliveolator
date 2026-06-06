using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Key;

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
        return new MusicTrack(file, result.Bpm, result.Key, result.Duration, result.Cues, status, null, metadata, kind);
    }

    // A track that fails to decode can still have readable tags, so capture metadata here too. With no
    // duration it classifies as a Track unless the file sits under a designated samples folder.
    protected override MusicTrack CreateFailedEntry(ScannedFile file, string error)
        => new(file, null, null, null, TrackCues.None, MediaAnalysisStatus.Failed, error,
               ReadMetadata(file.Path), SampleClassifier.Classify(file.Path, null, _sampleFolders));

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
