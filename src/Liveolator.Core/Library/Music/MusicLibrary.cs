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

    public MusicLibrary(IFileEnumerator enumerator, IAudioDecoder decoder, TrackAnalyzer? analyzer = null)
        : base(enumerator)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _analyzer = analyzer ?? new TrackAnalyzer();
    }

    protected override IReadOnlySet<string> Extensions => AudioExtensions;

    protected override async Task<MusicTrack> CreateEntryAsync(ScannedFile file, CancellationToken cancellationToken)
    {
        TrackAnalysisResult result = await _analyzer
            .AnalyzeAsync(_decoder, file.Path, cancellationToken)
            .ConfigureAwait(false);
        MediaAnalysisStatus status = TrackStatusPolicy.For(result);
        return new MusicTrack(file, result.Bpm, result.Key, result.Duration, result.Cues, status, null);
    }

    protected override MusicTrack CreateFailedEntry(ScannedFile file, string error)
        => new(file, null, null, null, TrackCues.None, MediaAnalysisStatus.Failed, error);

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
}
