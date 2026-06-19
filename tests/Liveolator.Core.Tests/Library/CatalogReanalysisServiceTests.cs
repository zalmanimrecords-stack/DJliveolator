using Liveolator.Core.Analysis;
using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Analysis.Key;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Xunit;

namespace Liveolator.Core.Tests.Library;

/// <summary>
/// The background re-analysis pass (<see cref="CatalogReanalysisService"/>): it must analyze only the
/// tracks that still lack analysis (Failed / no BPM), leave already-analyzed tracks untouched (no
/// re-decode), survive a file that fails to decode, persist the updated catalog, and report progress —
/// without ever throwing out of the loop (global standards #16/#26).
/// </summary>
public class CatalogReanalysisServiceTests
{
    private static readonly DateTime T = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static MusicTrack Failed(string path)
        => new(new ScannedFile(path, 10, T), null, null, null, TrackCues.None, MediaAnalysisStatus.Failed, "old ffmpeg error");

    private static MusicTrack Analyzed(string path, double bpm)
    {
        var key = new MusicalKey(0, KeyMode.Major, Camelot.Code(0, KeyMode.Major), 0.9);
        return new MusicTrack(
            new ScannedFile(path, 10, T), new BpmResult(bpm, 0.9), key,
            TimeSpan.FromMinutes(4), TrackCues.None, MediaAnalysisStatus.Ok, null,
            AnalyzerVersion: TrackAnalyzer.CurrentVersion);
    }

    private static MusicLibrary LibraryWith(IAudioDecoder decoder, params MusicTrack[] tracks)
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), decoder);
        library.Restore(tracks);
        return library;
    }

    [Fact]
    public async Task RunAsync_AnalyzesOnlyUnanalyzedTracks_AndPersists()
    {
        // a + c need analysis (Failed); b is already Ok. c cannot decode (maps to null → throws).
        var decoder = new MapAudioDecoder(new()
        {
            ["a.wav"] = TestSignals.ClickTrain(120.0, 44100, seconds: 10),
            ["c.wav"] = null,
        });
        MusicLibrary library = LibraryWith(decoder, Failed("a.wav"), Analyzed("b.wav", 128.0), Failed("c.wav"));
        var store = new RecordingCatalogStore();
        var progress = new List<ReanalysisProgress>();

        var service = new CatalogReanalysisService(library, store);
        ReanalysisOutcome outcome = await service.RunAsync(new TestProgress<ReanalysisProgress>(progress.Add));

        // a was decoded and now has a real BPM; b was skipped entirely; c stayed Failed (decode threw).
        Assert.InRange(library.TryGet("a.wav")!.Bpm!.Bpm, 117.0, 123.0);
        Assert.NotEqual(MediaAnalysisStatus.Failed, library.TryGet("a.wav")!.Status);
        Assert.False(decoder.DecodeCalls.ContainsKey("b.wav")); // never re-decoded an analyzed track
        Assert.Equal(128.0, library.TryGet("b.wav")!.Bpm!.Bpm);
        Assert.Equal(MediaAnalysisStatus.Failed, library.TryGet("c.wav")!.Status);

        Assert.Equal(2, outcome.Considered); // a + c
        Assert.Equal(1, outcome.Analyzed);   // only a succeeded
        Assert.Equal(2, progress[^1].Total);
        Assert.Equal(2, progress[^1].Done);

        // The updated catalog was persisted at least once, carrying a's new BPM.
        Assert.NotEmpty(store.Saved);
        MusicTrack savedA = store.Saved[^1].Single(t => t.File.Path == "a.wav");
        Assert.NotNull(savedA.Bpm);
    }

    [Fact]
    public async Task RunAsync_WhenNothingNeedsAnalysis_DoesNoWork()
    {
        var decoder = new MapAudioDecoder(new());
        MusicLibrary library = LibraryWith(decoder, Analyzed("x.wav", 124.0));
        var store = new RecordingCatalogStore();

        ReanalysisOutcome outcome = await new CatalogReanalysisService(library, store).RunAsync();

        Assert.Equal(0, outcome.Considered);
        Assert.Equal(0, outcome.Analyzed);
        Assert.Empty(decoder.DecodeCalls);
        Assert.Empty(store.Saved); // nothing changed → nothing persisted
    }

    [Fact]
    public async Task RunAsync_Cancellation_PersistsProgressAndStops()
    {
        var decoder = new MapAudioDecoder(new()
        {
            ["a.wav"] = TestSignals.ClickTrain(120.0, 44100, seconds: 10),
            ["b.wav"] = TestSignals.ClickTrain(120.0, 44100, seconds: 10),
        });
        MusicLibrary library = LibraryWith(decoder, Failed("a.wav"), Failed("b.wav"));
        var store = new RecordingCatalogStore();
        using var cts = new CancellationTokenSource();

        // Cancel as soon as the first track is persisted/analyzed so the run stops mid-way but keeps a's work.
        var service = new CatalogReanalysisService(library, store, persistEvery: 1);
        var progress = new TestProgress<ReanalysisProgress>(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(progress, cts.Token));

        Assert.NotEmpty(store.Saved); // partial progress was saved (resumable on next run)
    }

    [Fact]
    public async Task RunAsync_ForceMode_RemapsAnalyzedTracks_ButSkipsManuallyCorrected()
    {
        // Both are already analyzed at the current version (NeedsAnalysis == false), so a normal pass
        // would do nothing. A forced "Rescan all" must re-decode the auto track yet leave the manual one
        // untouched (its hand-set grid is protected, global #7).
        var decoder = new MapAudioDecoder(new()
        {
            ["auto.wav"] = TestSignals.ClickTrain(120.0, 44100, seconds: 10),
            ["manual.wav"] = TestSignals.ClickTrain(120.0, 44100, seconds: 10),
        });
        MusicLibrary library = LibraryWith(decoder, Analyzed("auto.wav", 128.0), Analyzed("manual.wav", 100.0));
        library.SetManualBeatGrid("manual.wav", bpm: 100.0, firstBeatSeconds: 0.2);
        var store = new RecordingCatalogStore();

        var service = new CatalogReanalysisService(library, store, force: true);
        ReanalysisOutcome outcome = await service.RunAsync();

        // auto.wav was re-decoded despite being analyzed, and now reflects the click train (~120).
        Assert.True(decoder.DecodeCalls.ContainsKey("auto.wav"));
        Assert.InRange(library.TryGet("auto.wav")!.Bpm!.Bpm, 117.0, 123.0);
        // manual.wav was never touched: not re-decoded, manual grid + lock intact.
        Assert.False(decoder.DecodeCalls.ContainsKey("manual.wav"));
        Assert.Equal(100.0, library.TryGet("manual.wav")!.Bpm!.Bpm);
        Assert.True(library.TryGet("manual.wav")!.AnalysisIsManual);

        Assert.Equal(1, outcome.Considered); // only the non-manual track is eligible
        Assert.Equal(1, outcome.Analyzed);
    }

    [Fact]
    public void PathsForFullRemap_ReturnsEveryTrackExceptManuallyCorrected()
    {
        MusicLibrary library = LibraryWith(
            new MapAudioDecoder(new()), Analyzed("a.wav", 120.0), Analyzed("b.wav", 124.0), Failed("c.wav"));
        library.SetManualBeatGrid("a.wav", bpm: 126.0, firstBeatSeconds: 0.1);

        IReadOnlyList<string> paths = library.PathsForFullRemap();

        Assert.DoesNotContain("a.wav", paths); // manual is protected
        Assert.Contains("b.wav", paths);
        Assert.Contains("c.wav", paths);       // even a failed track is eligible for a full remap
    }

    [Fact]
    public void NeedsAnalysis_WhenAnalyzerVersionIsStale_ReturnsTrue()
    {
        MusicTrack stale = Analyzed("old.wav", 124.0) with { AnalyzerVersion = TrackAnalyzer.CurrentVersion - 1 };

        Assert.True(MusicLibrary.NeedsAnalysis(stale));
    }

    [Fact]
    public void SetManualBeatGrid_LocksTrackAgainstAutomaticReanalysis()
    {
        MusicLibrary library = LibraryWith(new MapAudioDecoder(new()), Analyzed("manual.wav", 124.0));

        Assert.True(library.SetManualBeatGrid("manual.wav", bpm: 126.5, firstBeatSeconds: 0.375));

        MusicTrack track = library.TryGet("manual.wav")!;
        Assert.Equal(126.5, track.Bpm!.Bpm);
        Assert.Equal(0.375, track.Bpm.FirstBeatSeconds);
        Assert.True(track.AnalysisIsManual);
        Assert.False(MusicLibrary.NeedsAnalysis(track with { AnalyzerVersion = 0 }));
    }
}

/// <summary>Captures the catalogs handed to SaveMusicAsync; the other seam members are inert.</summary>
internal sealed class RecordingCatalogStore : IMusicCatalogStore
{
    public List<IReadOnlyList<MusicTrack>> Saved { get; } = new();

    public Task SaveMusicAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken = default)
    {
        Saved.Add(tracks.ToList());
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MusicTrack>> LoadMusicAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MusicTrack>>(Array.Empty<MusicTrack>());

    public Task<IReadOnlyList<string>> LoadScanFoldersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task SaveScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> LoadSampleFoldersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task SaveSampleFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Minimal synchronous <see cref="IProgress{T}"/> so reported values can be asserted/acted on inline.</summary>
internal sealed class TestProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;
    public TestProgress(Action<T> onReport) => _onReport = onReport;
    public void Report(T value) => _onReport(value);
}
